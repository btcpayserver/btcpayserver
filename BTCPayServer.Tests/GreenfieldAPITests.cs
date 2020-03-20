using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Services;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Xunit.Abstractions;
using CreateApplicationUserRequest = BTCPayServer.Client.Models.CreateApplicationUserRequest;

namespace BTCPayServer.Tests
{
    public class GreenfieldAPITests
    {
        public const int TestTimeout = TestUtils.TestTimeout;

        public const string TestApiPath = "api/test/apikey";

        public GreenfieldAPITests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task ApiKeysControllerTests()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                var client = await user.CreateClient(Policies.Unrestricted);
                //Get current api key 
                var apiKeyData = await client.GetCurrentAPIKeyInfo();
                Assert.NotNull(apiKeyData);
                Assert.Equal(client.APIKey, apiKeyData.ApiKey);
                Assert.Single(apiKeyData.Permissions);

                //revoke current api key
                await client.RevokeCurrentAPIKeyInfo();
                await AssertHttpError(401, async () => await client.GetCurrentAPIKeyInfo());
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCreateUsersViaAPI()
        {
            using (var tester = ServerTester.Create(newDb: true))
            {
                tester.PayTester.DisableRegistration = true;
                await tester.StartAsync();
                var unauthClient = new BTCPayServerClient(tester.PayTester.ServerUri);
                await AssertHttpError(400, async () => await unauthClient.CreateUser(new CreateApplicationUserRequest()));
                await AssertHttpError(400, async () => await unauthClient.CreateUser(new CreateApplicationUserRequest() { Email = "test@gmail.com" }));
                // Pass too simple
                await AssertHttpError(400, async () => await unauthClient.CreateUser(new CreateApplicationUserRequest() { Email = "test3@gmail.com", Password = "a" }));

                // We have no admin, so it should work
                var user1 = await unauthClient.CreateUser(new CreateApplicationUserRequest() { Email = "test@gmail.com", Password = "abceudhqw" });
                // We have no admin, so it should work
                var user2 = await unauthClient.CreateUser(new CreateApplicationUserRequest() { Email = "test2@gmail.com", Password = "abceudhqw" });

                // Duplicate email
                await AssertHttpError(400, async () => await unauthClient.CreateUser(new CreateApplicationUserRequest() { Email = "test2@gmail.com", Password = "abceudhqw" }));

                // Let's make an admin
                var admin = await unauthClient.CreateUser(new CreateApplicationUserRequest() { Email = "admin@gmail.com", Password = "abceudhqw", IsAdministrator = true });

                // Creating a new user without proper creds is now impossible (unauthorized) 
                // Because if registration are locked and that an admin exists, we don't accept unauthenticated connection
                await AssertHttpError(401, async () => await unauthClient.CreateUser(new CreateApplicationUserRequest() { Email = "test3@gmail.com", Password = "afewfoiewiou" }));


                // But should be ok with subscriptions unlocked
                var settings = tester.PayTester.GetService<SettingsRepository>();
                await settings.UpdateSetting<PoliciesSettings>(new PoliciesSettings() { LockSubscription = false });
                await unauthClient.CreateUser(new CreateApplicationUserRequest() { Email = "test3@gmail.com", Password = "afewfoiewiou" });

                // But it should be forbidden to create an admin without being authenticated
                await AssertHttpError(403, async () => await unauthClient.CreateUser(new CreateApplicationUserRequest() { Email = "admin2@gmail.com", Password = "afewfoiewiou", IsAdministrator = true }));
                await settings.UpdateSetting<PoliciesSettings>(new PoliciesSettings() { LockSubscription = true });

                var adminAcc = tester.NewAccount();
                adminAcc.UserId = admin.Id;
                adminAcc.IsAdmin = true;
                var adminClient = await adminAcc.CreateClient(Policies.CanModifyProfile);

                // We should be forbidden to create a new user without proper admin permissions
                await AssertHttpError(403, async () => await adminClient.CreateUser(new CreateApplicationUserRequest() { Email = "test4@gmail.com", Password = "afewfoiewiou" }));
                await AssertHttpError(403, async () => await adminClient.CreateUser(new CreateApplicationUserRequest() { Email = "test4@gmail.com", Password = "afewfoiewiou", IsAdministrator = true }));

                // However, should be ok with the unrestricted permissions of an admin
                adminClient = await adminAcc.CreateClient(Policies.Unrestricted);
                await adminClient.CreateUser(new CreateApplicationUserRequest() { Email = "test4@gmail.com", Password = "afewfoiewiou" });
                // Even creating new admin should be ok
                await adminClient.CreateUser(new CreateApplicationUserRequest() { Email = "admin4@gmail.com", Password = "afewfoiewiou", IsAdministrator = true });

                var user1Acc = tester.NewAccount();
                user1Acc.UserId = user1.Id;
                user1Acc.IsAdmin = false;
                var user1Client = await user1Acc.CreateClient(Policies.CanModifyServerSettings);
                // User1 trying to get server management would still fail to create user
                await AssertHttpError(403, async () => await user1Client.CreateUser(new CreateApplicationUserRequest() { Email = "test8@gmail.com", Password = "afewfoiewiou" }));

                // User1 should be able to create user if subscription unlocked
                await settings.UpdateSetting<PoliciesSettings>(new PoliciesSettings() { LockSubscription = false });
                await user1Client.CreateUser(new CreateApplicationUserRequest() { Email = "test8@gmail.com", Password = "afewfoiewiou" });
                // But not an admin
                await AssertHttpError(403, async () => await user1Client.CreateUser(new CreateApplicationUserRequest() { Email = "admin8@gmail.com", Password = "afewfoiewiou", IsAdministrator = true }));
            }
        }

        private async Task AssertHttpError(int code, Func<Task> act)
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(act);
            Assert.Contains(code.ToString(), ex.Message);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task UsersControllerTests()
        {
            using (var tester = ServerTester.Create(newDb: true))
            {
                tester.PayTester.DisableRegistration = true;
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                var clientProfile = await user.CreateClient(Policies.CanModifyProfile);
                var clientServer = await user.CreateClient(Policies.CanCreateUser, Policies.CanViewProfile);
                var clientInsufficient = await user.CreateClient(Policies.CanModifyStoreSettings);


                var apiKeyProfileUserData = await clientProfile.GetCurrentUser();
                Assert.NotNull(apiKeyProfileUserData);
                Assert.Equal(apiKeyProfileUserData.Id, user.UserId);
                Assert.Equal(apiKeyProfileUserData.Email, user.RegisterDetails.Email);

                await Assert.ThrowsAsync<HttpRequestException>(async () => await clientInsufficient.GetCurrentUser());
                await clientServer.GetCurrentUser();
                await clientProfile.GetCurrentUser();

                await Assert.ThrowsAsync<HttpRequestException>(async () => await clientInsufficient.CreateUser(new CreateApplicationUserRequest()
                {
                    Email = $"{Guid.NewGuid()}@g.com",
                    Password = Guid.NewGuid().ToString()
                }));

                var newUser = await clientServer.CreateUser(new CreateApplicationUserRequest()
                {
                    Email = $"{Guid.NewGuid()}@g.com",
                    Password = Guid.NewGuid().ToString()
                });
                Assert.NotNull(newUser);

                await Assert.ThrowsAsync<HttpRequestException>(async () => await clientServer.CreateUser(new CreateApplicationUserRequest()
                {
                    Email = $"{Guid.NewGuid()}",
                    Password = Guid.NewGuid().ToString()
                }));

                await Assert.ThrowsAsync<HttpRequestException>(async () => await clientServer.CreateUser(new CreateApplicationUserRequest()
                {
                    Email = $"{Guid.NewGuid()}@g.com",
                }));

                await Assert.ThrowsAsync<HttpRequestException>(async () => await clientServer.CreateUser(new CreateApplicationUserRequest()
                {
                    Password = Guid.NewGuid().ToString()
                }));

            }
        }
    }
}
