using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.JsonConverters;
using BTCPayServer.Services;
using BTCPayServer.Tests.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using CreateApplicationUserRequest = BTCPayServer.Client.Models.CreateApplicationUserRequest;
using JsonReader = Newtonsoft.Json.JsonReader;

namespace BTCPayServer.Tests
{
    public class GreenfieldAPITests
    {
        public const int TestTimeout = TestUtils.TestTimeout;

        public const string TestApiPath = "api/test/apikey";

        public GreenfieldAPITests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
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
                var client = await user.CreateClient(Policies.CanViewProfile);
                var clientBasic = await user.CreateClient();
                //Get current api key 
                var apiKeyData = await client.GetCurrentAPIKeyInfo();
                Assert.NotNull(apiKeyData);
                Assert.Equal(client.APIKey, apiKeyData.ApiKey);
                Assert.Single(apiKeyData.Permissions);

                //a client using Basic Auth has no business here
                await AssertHttpError(401, async () => await clientBasic.GetCurrentAPIKeyInfo());

                //revoke current api key
                await client.RevokeCurrentAPIKeyInfo();
                await AssertHttpError(401, async () => await client.GetCurrentAPIKeyInfo());
                //a client using Basic Auth has no business here
                await AssertHttpError(401, async () => await clientBasic.RevokeCurrentAPIKeyInfo());
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCreateAndDeleteAPIKeyViaAPI()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                await acc.GrantAccessAsync();
                var unrestricted = await acc.CreateClient();
                var apiKey = await unrestricted.CreateAPIKey(new CreateApiKeyRequest()
                {
                    Label = "Hello world",
                    Permissions = new Permission[] {Permission.Create(Policies.CanViewProfile)}
                });
                Assert.Equal("Hello world", apiKey.Label);
                var p = Assert.Single(apiKey.Permissions);
                Assert.Equal(Policies.CanViewProfile, p.Policy);

                var restricted = acc.CreateClientFromAPIKey(apiKey.ApiKey);
                await AssertHttpError(403,
                    async () => await restricted.CreateAPIKey(new CreateApiKeyRequest()
                    {
                        Label = "Hello world2",
                        Permissions = new Permission[] {Permission.Create(Policies.CanViewProfile)}
                    }));

                await unrestricted.RevokeAPIKey(apiKey.ApiKey);
                await AssertHttpError(404, async () => await unrestricted.RevokeAPIKey(apiKey.ApiKey));
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
                await AssertHttpError(400,
                    async () => await unauthClient.CreateUser(new CreateApplicationUserRequest()));
                await AssertHttpError(400,
                    async () => await unauthClient.CreateUser(
                        new CreateApplicationUserRequest() {Email = "test@gmail.com"}));
                // Pass too simple
                await AssertHttpError(400,
                    async () => await unauthClient.CreateUser(
                        new CreateApplicationUserRequest() {Email = "test3@gmail.com", Password = "a"}));

                // We have no admin, so it should work
                var user1 = await unauthClient.CreateUser(
                    new CreateApplicationUserRequest() {Email = "test@gmail.com", Password = "abceudhqw"});
                // We have no admin, so it should work
                var user2 = await unauthClient.CreateUser(
                    new CreateApplicationUserRequest() {Email = "test2@gmail.com", Password = "abceudhqw"});

                // Duplicate email
                await AssertHttpError(400,
                    async () => await unauthClient.CreateUser(
                        new CreateApplicationUserRequest() {Email = "test2@gmail.com", Password = "abceudhqw"}));

                // Let's make an admin
                var admin = await unauthClient.CreateUser(new CreateApplicationUserRequest()
                {
                    Email = "admin@gmail.com", Password = "abceudhqw", IsAdministrator = true
                });

                // Creating a new user without proper creds is now impossible (unauthorized) 
                // Because if registration are locked and that an admin exists, we don't accept unauthenticated connection
                await AssertHttpError(401,
                    async () => await unauthClient.CreateUser(
                        new CreateApplicationUserRequest() {Email = "test3@gmail.com", Password = "afewfoiewiou"}));


                // But should be ok with subscriptions unlocked
                var settings = tester.PayTester.GetService<SettingsRepository>();
                await settings.UpdateSetting<PoliciesSettings>(new PoliciesSettings() {LockSubscription = false});
                await unauthClient.CreateUser(
                    new CreateApplicationUserRequest() {Email = "test3@gmail.com", Password = "afewfoiewiou"});

                // But it should be forbidden to create an admin without being authenticated
                await AssertHttpError(403,
                    async () => await unauthClient.CreateUser(new CreateApplicationUserRequest()
                    {
                        Email = "admin2@gmail.com", Password = "afewfoiewiou", IsAdministrator = true
                    }));
                await settings.UpdateSetting<PoliciesSettings>(new PoliciesSettings() {LockSubscription = true});

                var adminAcc = tester.NewAccount();
                adminAcc.UserId = admin.Id;
                adminAcc.IsAdmin = true;
                var adminClient = await adminAcc.CreateClient(Policies.CanModifyProfile);

                // We should be forbidden to create a new user without proper admin permissions
                await AssertHttpError(403,
                    async () => await adminClient.CreateUser(
                        new CreateApplicationUserRequest() {Email = "test4@gmail.com", Password = "afewfoiewiou"}));
                await AssertHttpError(403,
                    async () => await adminClient.CreateUser(new CreateApplicationUserRequest()
                    {
                        Email = "test4@gmail.com", Password = "afewfoiewiou", IsAdministrator = true
                    }));

                // However, should be ok with the unrestricted permissions of an admin
                adminClient = await adminAcc.CreateClient(Policies.Unrestricted);
                await adminClient.CreateUser(
                    new CreateApplicationUserRequest() {Email = "test4@gmail.com", Password = "afewfoiewiou"});
                // Even creating new admin should be ok
                await adminClient.CreateUser(new CreateApplicationUserRequest()
                {
                    Email = "admin4@gmail.com", Password = "afewfoiewiou", IsAdministrator = true
                });

                var user1Acc = tester.NewAccount();
                user1Acc.UserId = user1.Id;
                user1Acc.IsAdmin = false;
                var user1Client = await user1Acc.CreateClient(Policies.CanModifyServerSettings);

                // User1 trying to get server management would still fail to create user
                await AssertHttpError(403,
                    async () => await user1Client.CreateUser(
                        new CreateApplicationUserRequest() {Email = "test8@gmail.com", Password = "afewfoiewiou"}));

                // User1 should be able to create user if subscription unlocked
                await settings.UpdateSetting<PoliciesSettings>(new PoliciesSettings() {LockSubscription = false});
                await user1Client.CreateUser(
                    new CreateApplicationUserRequest() {Email = "test8@gmail.com", Password = "afewfoiewiou"});

                // But not an admin
                await AssertHttpError(403,
                    async () => await user1Client.CreateUser(new CreateApplicationUserRequest()
                    {
                        Email = "admin8@gmail.com", Password = "afewfoiewiou", IsAdministrator = true
                    }));
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task StoresControllerTests()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                var client = await user.CreateClient(Policies.Unrestricted);

                //create store
                var newStore = await client.CreateStore(new CreateStoreRequest() {Name = "A"});

                //update store
                var updatedStore = await client.UpdateStore(newStore.Id, new UpdateStoreRequest() {Name = "B"});
                Assert.Equal("B", updatedStore.Name);
                Assert.Equal("B", (await client.GetStore(newStore.Id)).Name);

                //list stores
                var stores = await client.GetStores();
                var storeIds = stores.Select(data => data.Id);
                var storeNames = stores.Select(data => data.Name);
                Assert.NotNull(stores);
                Assert.Equal(2, stores.Count());
                Assert.Contains(newStore.Id, storeIds);
                Assert.Contains(user.StoreId, storeIds);

                //get store
                var store = await client.GetStore(user.StoreId);
                Assert.Equal(user.StoreId, store.Id);
                Assert.Contains(store.Name, storeNames);

                //remove store
                await client.RemoveStore(newStore.Id);
                await AssertHttpError(403, async () =>
                {
                    await client.GetStore(newStore.Id);
                });
                Assert.Single(await client.GetStores());

                newStore = await client.CreateStore(new CreateStoreRequest() {Name = "A"});
                var scopedClient =
                    await user.CreateClient(Permission.Create(Policies.CanViewStoreSettings, user.StoreId).ToString());
                Assert.Single(await scopedClient.GetStores());
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
                var clientBasic = await user.CreateClient();


                var apiKeyProfileUserData = await clientProfile.GetCurrentUser();
                Assert.NotNull(apiKeyProfileUserData);
                Assert.Equal(apiKeyProfileUserData.Id, user.UserId);
                Assert.Equal(apiKeyProfileUserData.Email, user.RegisterDetails.Email);

                await Assert.ThrowsAsync<HttpRequestException>(async () => await clientInsufficient.GetCurrentUser());
                await clientServer.GetCurrentUser();
                await clientProfile.GetCurrentUser();
                await clientBasic.GetCurrentUser();

                await Assert.ThrowsAsync<HttpRequestException>(async () =>
                    await clientInsufficient.CreateUser(new CreateApplicationUserRequest()
                    {
                        Email = $"{Guid.NewGuid()}@g.com", Password = Guid.NewGuid().ToString()
                    }));

                var newUser = await clientServer.CreateUser(new CreateApplicationUserRequest()
                {
                    Email = $"{Guid.NewGuid()}@g.com", Password = Guid.NewGuid().ToString()
                });
                Assert.NotNull(newUser);

                var newUser2 = await clientBasic.CreateUser(new CreateApplicationUserRequest()
                {
                    Email = $"{Guid.NewGuid()}@g.com", Password = Guid.NewGuid().ToString()
                });
                Assert.NotNull(newUser2);

                await Assert.ThrowsAsync<HttpRequestException>(async () =>
                    await clientServer.CreateUser(new CreateApplicationUserRequest()
                    {
                        Email = $"{Guid.NewGuid()}", Password = Guid.NewGuid().ToString()
                    }));

                await Assert.ThrowsAsync<HttpRequestException>(async () =>
                    await clientServer.CreateUser(
                        new CreateApplicationUserRequest() {Email = $"{Guid.NewGuid()}@g.com",}));

                await Assert.ThrowsAsync<HttpRequestException>(async () =>
                    await clientServer.CreateUser(
                        new CreateApplicationUserRequest() {Password = Guid.NewGuid().ToString()}));
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task HealthControllerTests()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var unauthClient = new BTCPayServerClient(tester.PayTester.ServerUri);

                var apiHealthData = await unauthClient.GetHealth();
                Assert.NotNull(apiHealthData);
                Assert.True(apiHealthData.Synchronized);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task ServerInfoControllerTests()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var unauthClient = new BTCPayServerClient(tester.PayTester.ServerUri);
                await AssertHttpError(401, async () => await unauthClient.GetServerInfo());

                var user = tester.NewAccount();
                user.GrantAccess();
                var clientBasic = await user.CreateClient();
                var serverInfoData = await clientBasic.GetServerInfo();
                Assert.NotNull(serverInfoData);
                Assert.NotNull(serverInfoData.Version);
                Assert.NotNull(serverInfoData.Onion);
                Assert.NotNull(serverInfoData.Status);
                Assert.True(serverInfoData.Status.FullySynched);
                Assert.Contains("BTC", serverInfoData.SupportedPaymentMethods);
                Assert.Contains("BTC_LightningLike", serverInfoData.SupportedPaymentMethods);
                Assert.NotNull(serverInfoData.Status.SyncStatus);
                Assert.Single(serverInfoData.Status.SyncStatus.Select(s => s.CryptoCode == "BTC"));
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task PaymentControllerTests()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                var client = await user.CreateClient(Policies.Unrestricted);
                var viewOnly = await user.CreateClient(Policies.CanViewPaymentRequests);

                //create payment request

                //validation errors
                await AssertHttpError(400, async () =>
                {
                    await client.CreatePaymentRequest(user.StoreId, new CreatePaymentRequestRequest() {Title = "A"});
                });
                await AssertHttpError(400, async () =>
                {
                    await client.CreatePaymentRequest(user.StoreId,
                        new CreatePaymentRequestRequest() {Title = "A", Currency = "BTC", Amount = 0});
                });
                await AssertHttpError(400, async () =>
                {
                    await client.CreatePaymentRequest(user.StoreId,
                        new CreatePaymentRequestRequest() {Title = "A", Currency = "helloinvalid", Amount = 1});
                });
                await AssertHttpError(403, async () =>
                {
                    await viewOnly.CreatePaymentRequest(user.StoreId,
                        new CreatePaymentRequestRequest() {Title = "A", Currency = "helloinvalid", Amount = 1});
                });
                var newPaymentRequest = await client.CreatePaymentRequest(user.StoreId,
                    new CreatePaymentRequestRequest() {Title = "A", Currency = "USD", Amount = 1});

                //list payment request
                var paymentRequests = await viewOnly.GetPaymentRequests(user.StoreId);

                Assert.NotNull(paymentRequests);
                Assert.Single(paymentRequests);
                Assert.Equal(newPaymentRequest.Id, paymentRequests.First().Id);

                //get payment request
                var paymentRequest = await viewOnly.GetPaymentRequest(user.StoreId, newPaymentRequest.Id);
                Assert.Equal(newPaymentRequest.Title, paymentRequest.Title);

                //update payment request
                var updateRequest = JObject.FromObject(paymentRequest).ToObject<UpdatePaymentRequestRequest>();
                updateRequest.Title = "B";
                await AssertHttpError(403, async () =>
                {
                    await viewOnly.UpdatePaymentRequest(user.StoreId, paymentRequest.Id, updateRequest);
                });
                await client.UpdatePaymentRequest(user.StoreId, paymentRequest.Id, updateRequest);
                paymentRequest = await client.GetPaymentRequest(user.StoreId, newPaymentRequest.Id);
                Assert.Equal(updateRequest.Title, paymentRequest.Title);

                //archive payment request
                await AssertHttpError(403, async () =>
                {
                    await viewOnly.ArchivePaymentRequest(user.StoreId, paymentRequest.Id);
                });

                await client.ArchivePaymentRequest(user.StoreId, paymentRequest.Id);
                Assert.DoesNotContain(paymentRequest.Id,
                    (await client.GetPaymentRequests(user.StoreId)).Select(data => data.Id));
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Fast", "Fast")]
        public async Task DecimalStringJsonConverterTests()
        {
            JsonReader Get(string val)
            {
                return new JsonTextReader(new StringReader(val));
            }

            var jsonConverter = new DecimalStringJsonConverter();
            Assert.True(jsonConverter.CanConvert(typeof(decimal)));
            Assert.True(jsonConverter.CanConvert(typeof(decimal?)));
            Assert.False(jsonConverter.CanConvert(typeof(double)));
            Assert.False(jsonConverter.CanConvert(typeof(float)));
            Assert.False(jsonConverter.CanConvert(typeof(int)));
            Assert.False(jsonConverter.CanConvert(typeof(string)));

            var numberJson = "1";
            var numberDecimalJson = "1.2";
            var stringJson = "\"1.2\"";
            Assert.Equal(1m, jsonConverter.ReadJson(Get(numberJson), typeof(decimal), null, null));
            Assert.Equal(1.2m, jsonConverter.ReadJson(Get(numberDecimalJson), typeof(decimal), null, null));
            Assert.Null(jsonConverter.ReadJson(Get("null"), typeof(decimal?), null, null));
            Assert.Throws<JsonSerializationException>(() =>
            {
                jsonConverter.ReadJson(Get("null"), typeof(decimal), null, null);
            });
            Assert.Equal(1.2m, jsonConverter.ReadJson(Get(stringJson), typeof(decimal), null, null));
            Assert.Equal(1.2m, jsonConverter.ReadJson(Get(stringJson), typeof(decimal?), null, null));
        }
    }
}
