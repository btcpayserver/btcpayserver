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

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task ApiKeysControllerTests()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                string apiKey = await GenerateAPIKey(tester, user);
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

        private static async Task<string> GenerateAPIKey(ServerTester tester, TestAccount user)
        {
            var manageController = tester.PayTester.GetController<ManageController>(user.UserId, user.StoreId, user.IsAdmin);
            var x = Assert.IsType<RedirectToActionResult>(await manageController.AddApiKey(
                new ManageController.AddApiKeyViewModel()
                {
                    ServerManagementPermission = true,
                    StoreManagementPermission = true,
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
