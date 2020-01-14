using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.U2F.Models;
using Microsoft.AspNetCore.Identity;
using NBitcoin;
using U2F.Core.Models;
using U2F.Core.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using U2F.Core.Exceptions;

namespace BTCPayServer.U2F
{
    public class U2FService
    {
        private readonly ApplicationDbContextFactory _contextFactory;

        private ConcurrentDictionary<string, List<U2FDeviceAuthenticationRequest>> UserAuthenticationRequests
        {
            get;
            set;
        }
            = new ConcurrentDictionary<string, List<U2FDeviceAuthenticationRequest>>();

        public U2FService(ApplicationDbContextFactory contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<U2FDevice>> GetDevices(string userId)
        {
            using (var context = _contextFactory.CreateContext())
            {
                return await context.U2FDevices
                    .Where(device => device.ApplicationUserId == userId)
                    .ToListAsync();
            }
        }

        public async Task RemoveDevice(string id, string userId)
        {
            using (var context = _contextFactory.CreateContext())
            {
                var device = await context.U2FDevices.FindAsync(id);
                if (device == null || !device.ApplicationUserId.Equals(userId, StringComparison.InvariantCulture))
                {
                    return;
                }

                context.U2FDevices.Remove(device);
                await context.SaveChangesAsync();
            }
        }
        
        public async Task<bool> HasDevices(string userId)
        {
            using (var context = _contextFactory.CreateContext())
            {
                return await context.U2FDevices.Where(fDevice => fDevice.ApplicationUserId == userId).AnyAsync();
            }
        }

        public ServerRegisterResponse StartDeviceRegistration(string userId, string appId)
        {
            var startedRegistration = StartDeviceRegistrationCore(appId);

            UserAuthenticationRequests.AddOrReplace(userId, new List<U2FDeviceAuthenticationRequest>()
            {
                new U2FDeviceAuthenticationRequest()
                {
                    AppId = startedRegistration.AppId,
                    Challenge = startedRegistration.Challenge,
                    Version = global::U2F.Core.Crypto.U2F.U2FVersion,
                }
            });

            return new ServerRegisterResponse
            {
                AppId = startedRegistration.AppId,
                Challenge = startedRegistration.Challenge,
                Version = startedRegistration.Version
            };
        }

        public async Task<bool> CompleteRegistration(string userId, string deviceResponse, string name)
        {
            if (string.IsNullOrWhiteSpace(deviceResponse))
                return false;

            if (!UserAuthenticationRequests.ContainsKey(userId) || !UserAuthenticationRequests[userId].Any())
            {
                return false;
            }

            var registerResponse = RegisterResponse.FromJson<RegisterResponse>(deviceResponse);

            //There is only 1 request when registering device
            var authenticationRequest = UserAuthenticationRequests[userId].First();

            var startedRegistration =
                new StartedRegistration(authenticationRequest.Challenge, authenticationRequest.AppId);
            var registration = FinishRegistrationCore(startedRegistration, registerResponse);

            UserAuthenticationRequests.AddOrReplace(userId, new List<U2FDeviceAuthenticationRequest>());
            using (var context = _contextFactory.CreateContext())
            {
                var duplicate = context.U2FDevices.Any(device =>
                    device.ApplicationUserId == userId &&
                    device.KeyHandle.Equals(registration.KeyHandle) &&
                    device.PublicKey.Equals(registration.PublicKey));

                if (duplicate)
                {
                    throw new U2fException("The U2F Device has already been registered with this user");
                }
                
                await context.U2FDevices.AddAsync(new U2FDevice()
                {
                    Id = Guid.NewGuid().ToString(),
                    AttestationCert = registration.AttestationCert,
                    Counter = Convert.ToInt32(registration.Counter),
                    Name = name,
                    KeyHandle = registration.KeyHandle,
                    PublicKey = registration.PublicKey,
                    ApplicationUserId = userId
                });

                await context.SaveChangesAsync();
            }

            return true;
        }

        public async Task<bool> AuthenticateUser(string userId, string deviceResponse)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(deviceResponse))
                return false;

            var authenticateResponse =
                AuthenticateResponse.FromJson<AuthenticateResponse>(deviceResponse);

            using (var context = _contextFactory.CreateContext())
            {
                var keyHandle = authenticateResponse.KeyHandle.Base64StringToByteArray();
                var device = await context.U2FDevices.Where(fDevice =>
                    fDevice.ApplicationUserId == userId &&
                    fDevice.KeyHandle == keyHandle).SingleOrDefaultAsync();

                if (device == null)
                    return false;

                // User will have a authentication request for each device they have registered so get the one that matches the device key handle

                var authenticationRequest =
                    UserAuthenticationRequests[userId].First(f =>
                        f.KeyHandle.Equals(authenticateResponse.KeyHandle, StringComparison.InvariantCulture));
                
                var registration = new DeviceRegistration(device.KeyHandle, device.PublicKey,
                    device.AttestationCert, Convert.ToUInt32(device.Counter));

                var authentication = new StartedAuthentication(authenticationRequest.Challenge,
                    authenticationRequest.AppId, authenticationRequest.KeyHandle);

                
                var challengeAuthenticationRequestMatch = UserAuthenticationRequests[userId].First(f =>
                    f.Challenge.Equals( authenticateResponse.GetClientData().Challenge, StringComparison.InvariantCulture));

                if (authentication.Challenge != challengeAuthenticationRequestMatch.Challenge)
                {
                    authentication = new StartedAuthentication(challengeAuthenticationRequestMatch.Challenge, authenticationRequest.AppId, authenticationRequest.KeyHandle);
                }
                FinishAuthenticationCore(authentication, authenticateResponse, registration);

                UserAuthenticationRequests.AddOrReplace(userId, new List<U2FDeviceAuthenticationRequest>());

                device.Counter = Convert.ToInt32(registration.Counter);
                await context.SaveChangesAsync();
            }

            return true;
        }

        public async Task<List<ServerChallenge>> GenerateDeviceChallenges(string userId, string appId)
        {
            using (var context = _contextFactory.CreateContext())
            {
                var devices = await context.U2FDevices.Where(fDevice => fDevice.ApplicationUserId == userId).ToListAsync();

                if (devices.Count == 0)
                    return null;

                var requests = new List<U2FDeviceAuthenticationRequest>();



                var serverChallenges = new List<ServerChallenge>();
                foreach (var registeredDevice in devices)
                {
                   var challenge =  StartAuthenticationCore(appId, registeredDevice);
                   serverChallenges.Add(new ServerChallenge()
                   {
                       challenge = challenge.Challenge,
                       appId = challenge.AppId,
                       version = challenge.Version,
                       keyHandle = challenge.KeyHandle
                   });
                   
                    requests.Add(
                        new U2FDeviceAuthenticationRequest()
                        {
                            AppId = appId,
                            Challenge = challenge.Challenge,
                            KeyHandle = registeredDevice.KeyHandle.ByteArrayToBase64String(),
                            Version = global::U2F.Core.Crypto.U2F.U2FVersion
                        });
                }

                UserAuthenticationRequests.AddOrReplace(userId, requests);
                return serverChallenges;
            }
        }

        protected virtual StartedRegistration StartDeviceRegistrationCore(string appId)
        {
            return global::U2F.Core.Crypto.U2F.StartRegistration(appId);
        }
        
        protected virtual DeviceRegistration FinishRegistrationCore(StartedRegistration startedRegistration, RegisterResponse registerResponse)
        {
            return global::U2F.Core.Crypto.U2F.FinishRegistration(startedRegistration, registerResponse);
        }
        
        protected virtual StartedAuthentication StartAuthenticationCore(string appId, U2FDevice registeredDevice)
        {
            return global::U2F.Core.Crypto.U2F.StartAuthentication(appId,
                new DeviceRegistration(registeredDevice.KeyHandle, registeredDevice.PublicKey,
                    registeredDevice.AttestationCert, (uint)registeredDevice.Counter));
        }
        
        protected virtual void FinishAuthenticationCore(StartedAuthentication authentication,
            AuthenticateResponse authenticateResponse, DeviceRegistration registration)
        {
            global::U2F.Core.Crypto.U2F.FinishAuthentication(authentication, authenticateResponse, registration);
        }
    }
}
