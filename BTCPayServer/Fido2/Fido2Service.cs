using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Fido2.Models;
using ExchangeSharp;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Fido2
{
    public class Fido2Service
    {
        private static readonly ConcurrentDictionary<string, CredentialCreateOptions> CreationStore =
            new ConcurrentDictionary<string, CredentialCreateOptions>();
        private static readonly ConcurrentDictionary<string, AssertionOptions> LoginStore =
            new ConcurrentDictionary<string, AssertionOptions>();
        private readonly ApplicationDbContextFactory _contextFactory;
        private readonly IFido2 _fido2;
        private readonly Fido2Configuration _fido2Configuration;

        public Fido2Service(ApplicationDbContextFactory contextFactory, IFido2 fido2, Fido2Configuration fido2Configuration)
        {
            _contextFactory = contextFactory;
            _fido2 = fido2;
            _fido2Configuration = fido2Configuration;
        }

        public async Task<CredentialCreateOptions> RequestCreation(string userId)
        {
            await using var dbContext = _contextFactory.CreateContext();
            var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
            if (user == null)
            {
                return null;
            }

            // 2. Get user existing keys by username
            var existingKeys =
                user.Fido2Credentials
                    .Where(credential => credential.Type == Fido2Credential.CredentialType.FIDO2)
                    .Select(c => c.GetFido2Blob().Descriptor).ToList();

            // 3. Create options
            var authenticatorSelection = new AuthenticatorSelection
            {
                RequireResidentKey = false,
                UserVerification = UserVerificationRequirement.Preferred
            };

            var exts = new AuthenticationExtensionsClientInputs()
            {
                Extensions = true,
                UserVerificationIndex = true,
                Location = true,
                UserVerificationMethod = true,
                BiometricAuthenticatorPerformanceBounds = new AuthenticatorBiometricPerfBounds
                {
                    FAR = float.MaxValue,
                    FRR = float.MaxValue
                },
            };

            var options = _fido2.RequestNewCredential(
                new Fido2User() { DisplayName = user.UserName, Name = user.UserName, Id = user.Id.ToBytesUTF8() },
                existingKeys, authenticatorSelection, AttestationConveyancePreference.None, exts);

            // options.Rp = new PublicKeyCredentialRpEntity(Request.Host.Host, options.Rp.Name, "");
            CreationStore.AddOrReplace(userId, options);
            return options;
        }

        public async Task<bool> CompleteCreation(string userId, string name, string data)
        {
            try
            {

                var attestationResponse = JObject.Parse(data).ToObject<AuthenticatorAttestationRawResponse>();
                await using var dbContext = _contextFactory.CreateContext();
                var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                    .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
                if (user == null || !CreationStore.TryGetValue(userId, out var options))
                {
                    return false;
                }

                // 2. Verify and make the credentials
                var success =
                    await _fido2.MakeNewCredentialAsync(attestationResponse, options, args => Task.FromResult(true));

                // 3. Store the credentials in db
                var newCredential = new Fido2Credential() { Name = name, ApplicationUserId = userId };

                newCredential.SetBlob(new Fido2CredentialBlob()
                {
                    Descriptor = new PublicKeyCredentialDescriptor(success.Result.CredentialId),
                    PublicKey = success.Result.PublicKey,
                    UserHandle = success.Result.User.Id,
                    SignatureCounter = success.Result.Counter,
                    CredType = success.Result.CredType,
                    AaGuid = success.Result.Aaguid.ToString(),
                });

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

        public async Task<List<Fido2Credential>> GetCredentials(string userId)
        {
            await using var context = _contextFactory.CreateContext();
            return await context.Fido2Credentials
                .Where(device => device.ApplicationUserId == userId)
                .ToListAsync();
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

        public async Task<bool> HasCredentials(string userId)
        {
            await using var context = _contextFactory.CreateContext();
            return await context.Fido2Credentials.Where(fDevice => fDevice.ApplicationUserId == userId && fDevice.Type == Fido2Credential.CredentialType.FIDO2).AnyAsync();
        }

        public async Task<AssertionOptions> RequestLogin(string userId)
        {
            await using var dbContext = _contextFactory.CreateContext();
            var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
            if (!(user?.Fido2Credentials?.Any() is true))
            {
                return null;
            }
            var existingCredentials = user.Fido2Credentials
                .Where(credential => credential.Type == Fido2Credential.CredentialType.FIDO2)
                .Select(c => c.GetFido2Blob().Descriptor)
                .ToList();
            var exts = new AuthenticationExtensionsClientInputs()
            {
                SimpleTransactionAuthorization = "FIDO",
                GenericTransactionAuthorization = new TxAuthGenericArg
                {
                    ContentType = "text/plain",
                    Content = new byte[] { 0x46, 0x49, 0x44, 0x4F }
                },
                UserVerificationIndex = true,
                Location = true,
                UserVerificationMethod = true,
                Extensions = true,
                AppID = _fido2Configuration.Origin
            };

            // 3. Create options
            var options = _fido2.GetAssertionOptions(
                existingCredentials,
                UserVerificationRequirement.Discouraged,
                exts
            );
            LoginStore.AddOrReplace(userId, options);
            return options;
        }

        public async Task<bool> CompleteLogin(string userId, AuthenticatorAssertionRawResponse response)
        {
            await using var dbContext = _contextFactory.CreateContext();
            var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
            if (user == null || !LoginStore.TryGetValue(userId, out var options))
            {
                return false;
            }

            var credential = user.Fido2Credentials
                .Where(fido2Credential => fido2Credential.Type is Fido2Credential.CredentialType.FIDO2)
                .Select(fido2Credential => (fido2Credential, fido2Credential.GetFido2Blob()))
                .FirstOrDefault(fido2Credential => fido2Credential.Item2.Descriptor.Id.SequenceEqual(response.Id));
            if (credential.Item2 is null)
            {
                return false;
            }

            // 5. Make the assertion
            var res = await _fido2.MakeAssertionAsync(response, options, credential.Item2.PublicKey,
                credential.Item2.SignatureCounter, x => Task.FromResult(true));

            // 6. Store the updated counter
            credential.Item2.SignatureCounter = res.Counter;
            credential.fido2Credential.SetBlob(credential.Item2);
            await dbContext.SaveChangesAsync();
            LoginStore.Remove(userId, out _);

            // 7. return OK to client
            return true;
        }
    }
}
