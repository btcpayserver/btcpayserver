using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Controllers.RestApi.ApiKeys;
using BTCPayServer.Data;
using BTCPayServer.Security.APIKeys;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
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
                
                //Get current api key 
                var request = new HttpRequestMessage(HttpMethod.Get,  "api/v1/api-keys/current");
                request.Headers.Authorization = new AuthenticationHeaderValue("token", apiKey);
                var result = await tester.PayTester.HttpClient.SendAsync(request);
                Assert.True(result.IsSuccessStatusCode);
                var apiKeyData = JObject.Parse(await result.Content.ReadAsStringAsync()).ToObject<ApiKeyData>();
                Assert.NotNull(apiKeyData);
                Assert.Equal(apiKey, apiKeyData.ApiKey);
                Assert.Equal(user.UserId, apiKeyData.UserId);
                Assert.Equal(2, apiKeyData.Permissions.Length);
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
