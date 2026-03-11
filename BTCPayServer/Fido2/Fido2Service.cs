using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Fido2.Models;
using Dapper;
using ExchangeSharp;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

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
                UserVerification = UserVerificationRequirement.Preferred,
                ResidentKey = ResidentKeyRequirement.Preferred
            };

            var exts = new AuthenticationExtensionsClientInputs()
            {
                Extensions = true,
                UserVerificationMethod = true
            };

            var options = _fido2.RequestNewCredential(new()
                {
                    User = new Fido2User() { DisplayName = user.UserName, Name = user.UserName, Id = user.Id.ToBytesUTF8() },
                    ExcludeCredentials = existingKeys,
                    AuthenticatorSelection = authenticatorSelection,
                    AttestationPreference = AttestationConveyancePreference.None,
                    Extensions = exts
                });
            // options.Rp = new PublicKeyCredentialRpEntity(Request.Host.Host, options.Rp.Name, "");
            CreationStore.AddOrReplace(userId, options);
            return options;
        }

        public async Task<bool> CompleteCreation(string userId, string name, string data)
        {
            try
            {

                var attestationResponse = System.Text.Json.JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(data);
                await using var dbContext = _contextFactory.CreateContext();
                var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                    .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
                if (user == null || !CreationStore.TryGetValue(userId, out var options))
                {
                    return false;
                }

                // 2. Verify and make the credentials
                var success =
                    await _fido2.MakeNewCredentialAsync(new() { AttestationResponse = attestationResponse, OriginalOptions = options, IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true)});

                // 3. Store the credentials in db
                var newCredential = new Fido2Credential() { Name = name, ApplicationUserId = userId };

                newCredential.SetBlob(new Fido2CredentialBlob()
                {
                    Descriptor = new PublicKeyCredentialDescriptor(success.Id),
                    PublicKey = success.PublicKey,
                    UserHandle = success.User.Id,
                    SignatureCounter = success.SignCount,
                    AaGuid = success.AaGuid.ToString(),
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
                UserVerificationMethod = true,
                Extensions = true,
                AppID = _fido2Configuration.Origins.First()
            };

            // 3. Create options
            var options = _fido2.GetAssertionOptions(
                new()
                {
                    AllowedCredentials = existingCredentials,
                    UserVerification = UserVerificationRequirement.Discouraged,
                    Extensions = exts
                }
            );
            LoginStore.AddOrReplace(userId, options);
            return options;
        }

        public async Task<bool> CompleteLogin(string userId, AuthenticatorAssertionRawResponse response)
        {
            await using var dbContext = _contextFactory.CreateContext();
            var user = await dbContext.Users.AsNoTracking()
                .Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
            if (user == null || !LoginStore.TryGetValue(userId, out var options))
                return false;
            var credential = user.Fido2Credentials
                .Where(fido2Credential => fido2Credential.Type is Fido2Credential.CredentialType.FIDO2)
                .Select(fido2Credential => (fido2Credential, fido2Credential.GetFido2Blob()))
                .FirstOrDefault(fido2Credential => fido2Credential.Item2.Descriptor.Id.SequenceEqual(response.RawId));
            if (credential.Item2 is null)
                return false;

            // 5. Make the assertion
            var res = await _fido2.MakeAssertionAsync(new()
            {
                AssertionResponse = response,
                OriginalOptions = options,
                StoredPublicKey = credential.Item2.PublicKey,
                StoredSignatureCounter = credential.Item2.SignatureCounter,
                IsUserHandleOwnerOfCredentialIdCallback = (a, _) => Task.FromResult(credential.Item1.ApplicationUserId == UTF8Encoding.UTF8.GetString(a.UserHandle))
            });

            // 6. Store the updated counter
            await dbContext.Fido2Credentials.GetDbConnection().ExecuteAsync("""
                UPDATE "Fido2Credentials"
                SET "Blob2" = jsonb_set(COALESCE("Blob2", '{}'::jsonb), '{signatureCounter}', to_jsonb(@signatureCounter))
                WHERE "Id" = @id
                """, new
            {
                id = credential.fido2Credential.Id,
                signatureCounter = (long)res.SignCount
            });
            LoginStore.Remove(userId, out _);

            // 7. return OK to client
            return true;
        }
    }
}
