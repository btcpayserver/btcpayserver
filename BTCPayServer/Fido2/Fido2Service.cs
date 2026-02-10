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
using static BTCPayServer.Fido2.Models.Fido2CredentialBlob;

namespace BTCPayServer.Fido2
{
    public class Fido2Service
    {
        private static readonly ConcurrentDictionary<string, CredentialCreateOptions> CreationStore =
            new ConcurrentDictionary<string, CredentialCreateOptions>();
        private static readonly ConcurrentDictionary<string, AssertionOptions> LoginStore =
            new ConcurrentDictionary<string, AssertionOptions>();
        // Store for passkey (discoverable) login challenges, keyed by challenge value
        private static readonly ConcurrentDictionary<string, AssertionOptions> PasskeyLoginStore =
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
                    .Select(c => c.GetFido2Blob().Descriptor?.ToFido2()).ToList();

            // 3. Create options
            var authenticatorSelection = new AuthenticatorSelection
            {
                RequireResidentKey = false,
                UserVerification = UserVerificationRequirement.Preferred
            };

            var exts = new AuthenticationExtensionsClientInputs()
            {
                Extensions = true,
                UserVerificationMethod = true
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
                    await _fido2.MakeNewCredentialAsync(attestationResponse, options, (args, cancellation) => Task.FromResult(true));

                // 3. Store the credentials in db
                var newCredential = new Fido2Credential() { Name = name, ApplicationUserId = userId };

                newCredential.SetBlob(new Fido2CredentialBlob()
                {
                    Descriptor = new DescriptorClass(success.Result.CredentialId),
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
                .Select(c => c.GetFido2Blob().Descriptor?.ToFido2())
                .ToList();
            var exts = new AuthenticationExtensionsClientInputs()
            {
                UserVerificationMethod = true,
                Extensions = true,
                AppID = _fido2Configuration.Origins.First()
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
                credential.Item2.SignatureCounter, (x, cancellationToken) => Task.FromResult(true));

            // 6. Store the updated counter
            credential.Item2.SignatureCounter = res.Counter;
            credential.fido2Credential.SetBlob(credential.Item2);
            await dbContext.SaveChangesAsync();
            LoginStore.Remove(userId, out _);

            // 7. return OK to client
            return true;
        }

        #region Passkey (Passwordless) Authentication

        /// <summary>
        /// Request passkey registration with discoverable credential support
        /// </summary>
        public async Task<CredentialCreateOptions> RequestPasskeyCreation(string userId)
        {
            await using var dbContext = _contextFactory.CreateContext();
            var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                .FirstOrDefaultAsync(applicationUser => applicationUser.Id == userId);
            if (user == null)
            {
                return null;
            }

            // Get user existing keys
            var existingKeys =
                user.Fido2Credentials
                    .Where(credential => credential.Type == Fido2Credential.CredentialType.FIDO2)
                    .Select(c => c.GetFido2Blob().Descriptor?.ToFido2()).ToList();

            // Create options with RequireResidentKey for discoverable credentials (passkeys)
            var authenticatorSelection = new AuthenticatorSelection
            {
                RequireResidentKey = true,
                UserVerification = UserVerificationRequirement.Preferred
            };

            var exts = new AuthenticationExtensionsClientInputs()
            {
                Extensions = true,
                UserVerificationMethod = true
            };

            var options = _fido2.RequestNewCredential(
                new Fido2User() { DisplayName = user.UserName, Name = user.UserName, Id = user.Id.ToBytesUTF8() },
                existingKeys, authenticatorSelection, AttestationConveyancePreference.None, exts);

            CreationStore.AddOrReplace(userId, options);
            return options;
        }

        /// <summary>
        /// Complete passkey registration and mark credential as discoverable
        /// </summary>
        public async Task<bool> CompletePasskeyCreation(string userId, string name, string data)
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

                // Verify and make the credentials
                var success =
                    await _fido2.MakeNewCredentialAsync(attestationResponse, options, (args, cancellation) => Task.FromResult(true));

                // Store the credentials in db with IsDiscoverable = true
                var newCredential = new Fido2Credential() { Name = name, ApplicationUserId = userId };

                newCredential.SetBlob(new Fido2CredentialBlob()
                {
                    Descriptor = new DescriptorClass(success.Result.CredentialId),
                    PublicKey = success.Result.PublicKey,
                    UserHandle = success.Result.User.Id,
                    SignatureCounter = success.Result.Counter,
                    CredType = success.Result.CredType,
                    AaGuid = success.Result.Aaguid.ToString(),
                    IsDiscoverable = true
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

        /// <summary>
        /// Check if user has any passkey (discoverable) credentials
        /// </summary>
        public async Task<bool> HasPasskeyCredentials(string userId)
        {
            await using var context = _contextFactory.CreateContext();
            var credentials = await context.Fido2Credentials
                .Where(fDevice => fDevice.ApplicationUserId == userId && fDevice.Type == Fido2Credential.CredentialType.FIDO2)
                .ToListAsync();
            return credentials.Any(c => c.GetFido2Blob()?.IsDiscoverable == true);
        }

        /// <summary>
        /// Request passkey login options - supports discoverable credentials (no userId required)
        /// </summary>
        public async Task<AssertionOptions> RequestPasskeyLogin(string email = null)
        {
            List<PublicKeyCredentialDescriptor> allowedCredentials = new List<PublicKeyCredentialDescriptor>();

            // If email is provided, get user's specific passkey credentials
            if (!string.IsNullOrWhiteSpace(email))
            {
                await using var dbContext = _contextFactory.CreateContext();
                var user = await dbContext.Users.Include(applicationUser => applicationUser.Fido2Credentials)
                    .FirstOrDefaultAsync(applicationUser => applicationUser.Email == email);

                if (user != null)
                {
                    allowedCredentials = user.Fido2Credentials
                        .Where(credential => credential.Type == Fido2Credential.CredentialType.FIDO2)
                        .Select(c => c.GetFido2Blob())
                        .Where(blob => blob?.IsDiscoverable == true)
                        .Select(blob => blob.Descriptor?.ToFido2())
                        .Where(d => d != null)
                        .ToList();
                }
            }
            // If no email, use empty allowedCredentials for discoverable flow
            // The browser will show all available passkeys for this relying party

            var exts = new AuthenticationExtensionsClientInputs()
            {
                UserVerificationMethod = true,
                Extensions = true
            };

            // Create assertion options
            var options = _fido2.GetAssertionOptions(
                allowedCredentials,
                UserVerificationRequirement.Preferred,
                exts
            );

            // Store options keyed by challenge value for discoverable flow
            var challengeKey = Convert.ToBase64String(options.Challenge);
            PasskeyLoginStore.AddOrReplace(challengeKey, options);

            return options;
        }

        /// <summary>
        /// Complete passkey login - looks up user by credential ID
        /// </summary>
        public async Task<(bool success, string userId, ApplicationUser user)> CompletePasskeyLogin(AuthenticatorAssertionRawResponse response)
        {
            try
            {
                await using var dbContext = _contextFactory.CreateContext();

                // Find the credential by credential ID
                var allCredentials = await dbContext.Fido2Credentials
                    .Include(c => c.ApplicationUser)
                    .Where(c => c.Type == Fido2Credential.CredentialType.FIDO2)
                    .ToListAsync();

                var matchedCredential = allCredentials
                    .Select(c => (credential: c, blob: c.GetFido2Blob()))
                    .FirstOrDefault(c => c.blob?.Descriptor?.Id != null && c.blob.Descriptor.Id.SequenceEqual(response.Id));

                if (matchedCredential.credential == null || matchedCredential.blob == null)
                {
                    return (false, null, null);
                }

                // Try to find the options by parsing the challenge from clientDataJSON
                AssertionOptions options = null;
                var clientDataJson = System.Text.Encoding.UTF8.GetString(response.Response.ClientDataJson);
                var clientData = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(clientDataJson);
                if (clientData.TryGetProperty("challenge", out var challengeElement))
                {
                    var challenge = challengeElement.GetString();
                    // Convert from base64url to base64
                    var base64Challenge = challenge.Replace('-', '+').Replace('_', '/');
                    // Pad if necessary
                    switch (base64Challenge.Length % 4)
                    {
                        case 2: base64Challenge += "=="; break;
                        case 3: base64Challenge += "="; break;
                    }
                    PasskeyLoginStore.TryGetValue(base64Challenge, out options);
                }

                if (options == null)
                {
                    return (false, null, null);
                }

                // Verify the assertion
                var res = await _fido2.MakeAssertionAsync(
                    response,
                    options,
                    matchedCredential.blob.PublicKey,
                    matchedCredential.blob.SignatureCounter,
                    (x, cancellationToken) => Task.FromResult(true));

                // Update the signature counter
                matchedCredential.blob.SignatureCounter = res.Counter;
                matchedCredential.credential.SetBlob(matchedCredential.blob);
                await dbContext.SaveChangesAsync();

                // Clean up the challenge store
                var challengeKey = Convert.ToBase64String(options.Challenge);
                PasskeyLoginStore.TryRemove(challengeKey, out _);

                return (true, matchedCredential.credential.ApplicationUserId, matchedCredential.credential.ApplicationUser);
            }
            catch (Exception)
            {
                return (false, null, null);
            }
        }

        #endregion
    }
}
