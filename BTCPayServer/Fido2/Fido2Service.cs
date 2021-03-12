using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
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

        public Fido2Service(ApplicationDbContextFactory contextFactory, IFido2 fido2)
        {
            _contextFactory = contextFactory;
            _fido2 = fido2;
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
                        .Select(c => c.GetDescriptor()).ToList();

                // 3. Create options
                var authenticatorSelection = new AuthenticatorSelection
                {
                    RequireResidentKey = false, UserVerification = UserVerificationRequirement.Preferred
                };

                var exts = new AuthenticationExtensionsClientInputs()
                {
                    Extensions = true,
                    UserVerificationIndex = true,
                    Location = true,
                    UserVerificationMethod = true,
                    BiometricAuthenticatorPerformanceBounds = new AuthenticatorBiometricPerfBounds
                    {
                        FAR = float.MaxValue, FRR = float.MaxValue
                    },
                };

                var options = _fido2.RequestNewCredential(
                    new Fido2User() {DisplayName = user.UserName, Name = user.UserName, Id = user.Id.ToBytesUTF8()},
                    existingKeys, authenticatorSelection, AttestationConveyancePreference.None, exts);

                // options.Rp = new PublicKeyCredentialRpEntity(Request.Host.Host, options.Rp.Name, "");
                CreationStore.AddOrReplace(userId, options);
                return options;
        }

        public async Task<bool> CompleteCreation(string userId, string name, AuthenticatorAttestationRawResponse attestationResponse)
        {
            await using var dbContext = _contextFactory.CreateContext();
            var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
            if (user == null || !CreationStore.TryGetValue(userId, out var options))
            {
                return false;
            }
            

                // 2. We want to allow users to have the same key (eg sharing a PC or device in a company)  
                IsCredentialIdUniqueToUserAsyncDelegate callback = (IsCredentialIdUniqueToUserParams args) => Task.FromResult(true);

                // 2. Verify and make the credentials
                var success = await _fido2.MakeNewCredentialAsync(attestationResponse, options, callback);

                // 3. Store the credentials in db
                var newCredential = new Fido2Credential()
                {
                    Name = name,
                    PublicKey = success.Result.PublicKey,
                    UserHandle = success.Result.User.Id,
                    SignatureCounter = success.Result.Counter,
                    CredType = success.Result.CredType,
                    RegDate = DateTime.Now,
                    AaGuid = success.Result.Aaguid.ToString(),
                    ApplicationUserId = userId
                };
                newCredential.SetDescriptor(new PublicKeyCredentialDescriptor(success.Result.CredentialId));

                await dbContext.Fido2Credentials.AddAsync(newCredential);
                await dbContext.SaveChangesAsync();
                CreationStore.Remove(userId, out _);
                return true;
        

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
            var device = await context.Fido2Credentials.FindAsync( id);
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
            return await context.Fido2Credentials.Where(fDevice => fDevice.ApplicationUserId == userId).AnyAsync();
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
            var existingCredentials = user.Fido2Credentials.Select(c => c.GetDescriptor()).ToList();
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
                UserVerificationMethod = true 
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
        
        public async Task<bool> CompleteLogin(string userId, AuthenticatorAssertionRawResponse response){
            await using var dbContext = _contextFactory.CreateContext();
            var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
            if (user == null || !LoginStore.TryGetValue(userId, out var options))
            {
                return false;
            }

            var credential = user.Fido2Credentials.FirstOrDefault(fido2Credential => fido2Credential.GetDescriptor().Id.SequenceEqual(response.Id));
            if (credential is null)
            {
                return false;
            }
            
            var storedCounter = credential.SignatureCounter;

            // 5. Make the assertion
            var res = await _fido2.MakeAssertionAsync(response, options, credential.PublicKey, storedCounter, x => Task.FromResult(true));

            // 6. Store the updated counter
            credential.SignatureCounter =  res.Counter;
            await dbContext.SaveChangesAsync();
            LoginStore.Remove(userId, out _);

            // 7. return OK to client
            return true;
        }
    }
}
