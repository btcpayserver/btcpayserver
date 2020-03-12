using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Xunit.Abstractions;

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
                string apiKey = await GenerateAPIKey(tester, user, Permissions.ServerManagement, Permissions.StoreManagement);
                var client = new BTCPayServerClient(tester.PayTester.ServerUri, apiKey);
                //Get current api key 
                var apiKeyData = await client.GetCurrentAPIKeyInfo();
                Assert.NotNull(apiKeyData);
                Assert.Equal(apiKey, apiKeyData.ApiKey);
                Assert.Equal(user.UserId, apiKeyData.UserId);
                Assert.Equal(2, apiKeyData.Permissions.Length);
                
                //revoke current api key
                await client.RevokeCurrentAPIKeyInfo();
                await Assert.ThrowsAsync<HttpRequestException>(async () =>
                {
                    await client.GetCurrentAPIKeyInfo();
                });
            }
        }
        
        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task UsersControllerTests()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                string apiKeyProfile = await GenerateAPIKey(tester, user, Permissions.ProfileManagement);
                string apiKeyInsufficient = await GenerateAPIKey(tester, user, Permissions.StoreManagement);
                var clientProfile = new BTCPayServerClient(tester.PayTester.ServerUri, apiKeyProfile);
                var clientInsufficient= new BTCPayServerClient(tester.PayTester.ServerUri, apiKeyInsufficient);
                
                var apiKeyProfileUserData = await clientProfile.GetCurrentUser();
                Assert.NotNull(apiKeyProfileUserData);
                Assert.Equal(apiKeyProfileUserData.Id, user.UserId);
                Assert.Equal(apiKeyProfileUserData.Email, user.RegisterDetails.Email);

                await Assert.ThrowsAsync<HttpRequestException>(async () => await clientInsufficient.GetCurrentUser());
            }
        }

        private static async Task<string> GenerateAPIKey(ServerTester tester, TestAccount user, params string[] permissions)
        {
            var manageController = tester.PayTester.GetController<ManageController>(user.UserId, user.StoreId, user.IsAdmin);
            var x = Assert.IsType<RedirectToActionResult>(await manageController.AddApiKey(
                new ManageController.AddApiKeyViewModel()
                {
                    PermissionValues = permissions.Select(s => new ManageController.AddApiKeyViewModel.PermissionValueItem()
                    {
                        Permission = s,
                        Value = true
                    }).ToList(),
                    StoreMode = ManageController.AddApiKeyViewModel.ApiKeyStoreMode.AllStores
                }));
            var statusMessage = manageController.TempData.GetStatusMessageModel();
            Assert.NotNull(statusMessage);
            var apiKey = statusMessage.Html.Substring(statusMessage.Html.IndexOf("<code>") + 6);
            apiKey = apiKey.Substring(0, apiKey.IndexOf("</code>") );
            return apiKey;
        }
    }
}
