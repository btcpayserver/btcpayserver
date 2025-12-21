using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Fido2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Crypto;

namespace BTCPayServer
{
    public class LoginWithLNURLAuthViewModel
    {
        public string UserId { get; set; }
        public Uri LNURLEndpoint { get; set; }
        public bool RememberMe { get; set; }
    }

    public class LnurlAuthService
    {
        public readonly ConcurrentDictionary<string, byte[]> CreationStore =
            new ConcurrentDictionary<string, byte[]>();
        public readonly ConcurrentDictionary<string, byte[]> LoginStore =
            new ConcurrentDictionary<string, byte[]>();
        public readonly ConcurrentDictionary<string, byte[]> FinalLoginStore =
            new ConcurrentDictionary<string, byte[]>();
        private readonly ApplicationDbContextFactory _contextFactory;
        private readonly ILogger<LnurlAuthService> _logger;

        public LnurlAuthService(ApplicationDbContextFactory contextFactory, ILogger<LnurlAuthService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<byte[]> RequestCreation(string userId)
        {
            await using var dbContext = _contextFactory.CreateContext();
            var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
            if (user == null)
            {
                return null;
            }
            var k1 = RandomUtils.GetBytes(32);
            CreationStore.AddOrReplace(userId, k1);
            return k1;
        }

        public async Task<bool> CompleteCreation(string name, string userId, ECDSASignature sig, PubKey pubKey)
        {
            try
            {
                await using var dbContext = _contextFactory.CreateContext();
                var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                    .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
                var pubkeyBytes = pubKey.ToBytes();
                if (!CreationStore.TryGetValue(userId.ToLowerInvariant(), out var k1) || user == null || await dbContext.Fido2Credentials.AnyAsync(credential => credential.Type == Fido2Credential.CredentialType.LNURLAuth && credential.Blob == pubkeyBytes))
                {
                    return false;
                }

                if (!LNURL.LNAuthRequest.VerifyChallenge(sig, pubKey, k1))
                {
                    return false;
                }

                var newCredential = new Fido2Credential() { Name = name, ApplicationUserId = userId, Type = Fido2Credential.CredentialType.LNURLAuth, Blob = pubkeyBytes };
                await dbContext.Fido2Credentials.AddAsync(newCredential);
                await dbContext.SaveChangesAsync();
                CreationStore.Remove(userId, out _);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task Remove(string id, string userId)
        {
            await using var context = _contextFactory.CreateContext();
            var device = await context.Fido2Credentials.FindAsync(id);
            if (device == null || !device.ApplicationUserId.Equals(userId, StringComparison.InvariantCulture))
            {
                return;
            }

            context.Fido2Credentials.Remove(device);
            await context.SaveChangesAsync();
        }


        public async Task<byte[]> RequestLogin(string userId)
        {
            await using var dbContext = _contextFactory.CreateContext();
            var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
            if (!(user?.Fido2Credentials?.Any(credential => credential.Type == Fido2Credential.CredentialType.LNURLAuth) is true))
            {
                return null;
            }

            var k1 = RandomUtils.GetBytes(32);

            FinalLoginStore.TryRemove(userId, out _);
            LoginStore.AddOrReplace(userId, k1);
            return k1;
        }

        public async Task<bool> CompleteLogin(string userId, ECDSASignature sig, PubKey pubKey)
        {
            await using var dbContext = _contextFactory.CreateContext();
            userId = userId.ToLowerInvariant();
            var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
            if (user == null || !LoginStore.TryGetValue(userId, out var k1))
            {
                return false;
            }

            var pubKeyBytes = pubKey.ToBytes();
            var credential = user.Fido2Credentials
                .Where(fido2Credential => fido2Credential.Type is Fido2Credential.CredentialType.LNURLAuth)
                .FirstOrDefault(fido2Credential => fido2Credential.Blob.SequenceEqual(pubKeyBytes));
            if (credential is null)
            {
                return false;
            }
            if (!LNURL.LNAuthRequest.VerifyChallenge(sig, pubKey, k1))
            {
                return false;
            }
            LoginStore.Remove(userId, out _);

            FinalLoginStore.AddOrReplace(userId, k1);
            // 7. return OK to client
            return true;
        }

        public async Task<bool> HasCredentials(string userId)
        {
            await using var context = _contextFactory.CreateContext();
            return await context.Fido2Credentials.Where(fDevice => fDevice.ApplicationUserId == userId && fDevice.Type == Fido2Credential.CredentialType.LNURLAuth).AnyAsync();
        }
    }

    public class LightningAddressQuery
    {
        public string[] StoreIds { get; set; }
        public string[] Usernames { get; set; }

    }
}
