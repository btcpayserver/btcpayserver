using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Views.Manage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;
using OpenQA.Selenium.Support.UI;
using Xunit;
using Xunit.Abstractions;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Tests
{
    public class ApiKeysTests : UnitTestBase
    {
        public const int TestTimeout = 120_000;

        public const string TestApiPath = "api/test/apikey";
        public ApiKeysTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Selenium", "Selenium")]
        public async Task CanCreateApiKeys()
        {
            //there are 2 ways to create api keys:
            //as a user through your profile
            //as an external application requesting an api key from a user

            using var s = CreateSeleniumTester();
            await s.StartAsync();
            var tester = s.Server;

            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            await user.MakeAdmin(false);
            s.GoToLogin();
            s.LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
            s.GoToProfile(ManageNavPages.APIKeys);
            s.Driver.FindElement(By.Id("AddApiKey")).Click();

            //not an admin, so this permission should not show
            Assert.DoesNotContain("btcpay.server.canmodifyserversettings", s.Driver.PageSource);
            await user.MakeAdmin();
            s.Logout();
            s.GoToLogin();
            s.LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
            s.GoToProfile(ManageNavPages.APIKeys);
            s.Driver.FindElement(By.Id("AddApiKey")).Click();
            Assert.Contains("btcpay.server.canmodifyserversettings", s.Driver.PageSource);

            //server management should show now
            s.Driver.SetCheckbox(By.Id("btcpay.server.canmodifyserversettings"), true);
            s.Driver.SetCheckbox(By.Id("btcpay.store.canmodifystoresettings"), true);
            s.Driver.SetCheckbox(By.Id("btcpay.user.canviewprofile"), true);
            s.Driver.FindElement(By.Id("Generate")).Click();
            var superApiKey = s.FindAlertMessage().FindElement(By.TagName("code")).Text;

            //this api key has access to everything
            await TestApiAgainstAccessToken(superApiKey, tester, user, Policies.CanModifyServerSettings, Policies.CanModifyStoreSettings, Policies.CanViewProfile);

            s.Driver.FindElement(By.Id("AddApiKey")).Click();
            s.Driver.SetCheckbox(By.Id("btcpay.server.canmodifyserversettings"), true);
            s.Driver.FindElement(By.Id("Generate")).Click();
            var serverOnlyApiKey = s.FindAlertMessage().FindElement(By.TagName("code")).Text;
            await TestApiAgainstAccessToken(serverOnlyApiKey, tester, user,
                Policies.CanModifyServerSettings);

            s.Driver.FindElement(By.Id("AddApiKey")).Click();
            s.Driver.SetCheckbox(By.Id("btcpay.store.canmodifystoresettings"), true);
            s.Driver.FindElement(By.Id("Generate")).Click();
            var allStoreOnlyApiKey = s.FindAlertMessage().FindElement(By.TagName("code")).Text;
            await TestApiAgainstAccessToken(allStoreOnlyApiKey, tester, user,
                Policies.CanModifyStoreSettings);

            s.Driver.FindElement(By.Id("AddApiKey")).Click();
            s.Driver.FindElement(By.CssSelector("button[value='btcpay.store.canmodifystoresettings:change-store-mode']")).Click();
            //there should be a store already by default in the dropdown
            var getPermissionValueIndex =
                s.Driver.FindElement(By.CssSelector("input[value='btcpay.store.canmodifystoresettings']"))
                    .GetAttribute("name")
                    .Replace(".Permission", ".SpecificStores[0]");
            var dropdown = s.Driver.FindElement(By.Name(getPermissionValueIndex));
            var option = dropdown.FindElement(By.TagName("option"));
            var storeId = option.GetAttribute("value");
            option.Click();
            s.Driver.WaitForAndClick(By.Id("Generate"));
            var selectiveStoreApiKey = s.FindAlertMessage().FindElement(By.TagName("code")).Text;
            await TestApiAgainstAccessToken(selectiveStoreApiKey, tester, user,
                Permission.Create(Policies.CanModifyStoreSettings, storeId).ToString());

            s.Driver.WaitForAndClick(By.Id("AddApiKey"));
            s.Driver.WaitForAndClick(By.Id("Generate"));
            var noPermissionsApiKey = s.FindAlertMessage().FindElement(By.TagName("code")).Text;
            await TestApiAgainstAccessToken(noPermissionsApiKey, tester, user);
            await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
            {
                await TestApiAgainstAccessToken<bool>("incorrect key", $"{TestApiPath}/me/id",
                    tester.PayTester.HttpClient);
            });

            TestLogs.LogInformation("Checking authorize screen");

            //let's test the authorized screen now
            //options for authorize are:
            //applicationName
            //redirect
            //permissions
            //strict
            //selectiveStores
            //redirect
            //appidentifier
            var appidentifier = "testapp";
            var callbackUrl = s.ServerUri + "postredirect-callback-test";
            var authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyServerSettings }, applicationDetails: (appidentifier, new Uri(callbackUrl))).ToString();

            // No upfront store selection with only server settings
            s.GoToUrl(authUrl);
            Assert.Contains(appidentifier, s.Driver.PageSource);
            Assert.True(s.Driver.ElementDoesNotExist(By.CssSelector("select#StoreId")));

            // No upfront store selection with selectiveStores being false
            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings }, selectiveStores: false, applicationDetails: (appidentifier, new Uri(callbackUrl))).ToString();
            s.GoToUrl(authUrl);
            Assert.True(s.Driver.ElementDoesNotExist(By.CssSelector("select#StoreId")));

            // Now with store settings
            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings }, selectiveStores: true, applicationDetails: (appidentifier, new Uri(callbackUrl))).ToString();
            s.GoToUrl(authUrl);
            Assert.Contains(appidentifier, s.Driver.PageSource);

            // Select a store
            var select = new SelectElement(s.Driver.FindElement(By.Id("StoreId")));
            select.SelectByIndex(0);
            s.Driver.FindElement(By.Id("continue")).Click();

            Assert.Equal("hidden", s.Driver.FindElement(By.Id("btcpay.store.canmodifystoresettings")).GetAttribute("type").ToLowerInvariant());
            Assert.Equal("true", s.Driver.FindElement(By.Id("btcpay.store.canmodifystoresettings")).GetAttribute("value").ToLowerInvariant());
            Assert.Equal("hidden", s.Driver.FindElement(By.Id("btcpay.server.canmodifyserversettings")).GetAttribute("type").ToLowerInvariant());
            Assert.Equal("true", s.Driver.FindElement(By.Id("btcpay.server.canmodifyserversettings")).GetAttribute("value").ToLowerInvariant());
            Assert.DoesNotContain("change-store-mode", s.Driver.PageSource);

            s.Driver.WaitForAndClick(By.Id("consent-yes"));
            Assert.Equal(callbackUrl, s.Driver.Url);

            var apiKeyRepo = s.Server.PayTester.GetService<APIKeyRepository>();
            var accessToken = GetAccessTokenFromCallbackResult(s.Driver);
            await TestApiAgainstAccessToken(accessToken, tester, user,
                (await apiKeyRepo.GetKey(accessToken)).GetBlob().Permissions);

            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings }, false, true, (null, new Uri(callbackUrl))).ToString();

            s.GoToUrl(authUrl);
            Assert.DoesNotContain("kukksappname", s.Driver.PageSource);

            // Select a store
            select = new SelectElement(s.Driver.FindElement(By.Id("StoreId")));
            select.SelectByIndex(0);
            s.Driver.FindElement(By.Id("continue")).Click();

            Assert.Equal("checkbox", s.Driver.FindElement(By.Id("btcpay.store.canmodifystoresettings")).GetAttribute("type").ToLowerInvariant());
            Assert.Equal("true", s.Driver.FindElement(By.Id("btcpay.store.canmodifystoresettings")).GetAttribute("value").ToLowerInvariant());
            Assert.Equal("checkbox", s.Driver.FindElement(By.Id("btcpay.server.canmodifyserversettings")).GetAttribute("type").ToLowerInvariant());
            Assert.Equal("true", s.Driver.FindElement(By.Id("btcpay.server.canmodifyserversettings")).GetAttribute("value").ToLowerInvariant());

            s.Driver.SetCheckbox(By.Id("btcpay.server.canmodifyserversettings"), false);
            s.Driver.WaitForAndClick(By.Id("consent-yes"));
            Assert.Equal(callbackUrl, s.Driver.Url);

            accessToken = GetAccessTokenFromCallbackResult(s.Driver);
            await TestApiAgainstAccessToken(accessToken, tester, user,
                (await apiKeyRepo.GetKey(accessToken)).GetBlob().Permissions);

            //let's test the app identifier system
            TestLogs.LogInformation("Checking app identifier system");
            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings }, false, true, (appidentifier, new Uri(callbackUrl))).ToString();

            //if it's the same, go to the confirm page
            s.GoToUrl(authUrl);

            // Select the same store
            select = new SelectElement(s.Driver.FindElement(By.Id("StoreId")));
            select.SelectByIndex(0);
            s.Driver.FindElement(By.Id("continue")).Click();

            Assert.Contains("previously generated the API Key", s.Driver.PageSource);
            s.Driver.WaitForAndClick(By.Id("continue"));
            Assert.Equal(callbackUrl, s.Driver.Url);

            //same app but different redirect = nono
            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings }, false, true, (appidentifier, new Uri("https://international.local/callback"))).ToString();

            s.GoToUrl(authUrl);

            // Select the same store
            select = new SelectElement(s.Driver.FindElement(By.Id("StoreId")));
            select.SelectByIndex(0);
            s.Driver.FindElement(By.Id("continue")).Click();

            Assert.DoesNotContain("previously generated the API Key", s.Driver.PageSource);
            Assert.False(s.Driver.Url.StartsWith("https://international.com/callback"));

            // Make sure we can check all permissions when not an admin
            TestLogs.LogInformation("Make sure we can check all permissions when not an admin");
            await user.MakeAdmin(false);
            s.Logout();
            s.GoToLogin();
            s.LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
            s.GoToUrl("/account/apikeys");
            s.Driver.WaitForAndClick(By.Id("AddApiKey"));
            int checkedPermissionCount = s.Driver.FindElements(By.ClassName("form-check-input")).Count;
            s.Driver.ExecuteJavaScript("document.querySelectorAll('#Permissions .form-check-input').forEach(i => i.click())");

            TestLogs.LogInformation("Generating API key");
            s.Driver.WaitForAndClick(By.Id("Generate"));
            var allAPIKey = s.FindAlertMessage().FindElement(By.TagName("code")).Text;

            TestLogs.LogInformation($"Checking API key permissions: {allAPIKey}");
            var apikeydata = await TestApiAgainstAccessToken<ApiKeyData>(allAPIKey, "api/v1/api-keys/current", tester.PayTester.HttpClient);
            Assert.Equal(checkedPermissionCount, apikeydata.Permissions.Length);

            TestLogs.LogInformation("Checking empty permissions");
            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri, Array.Empty<string>(), false, true).ToString();
            s.GoToUrl(authUrl);
            select = new SelectElement(s.Driver.FindElement(By.Id("StoreId")));
            select.SelectByIndex(0);
            s.Driver.FindElement(By.Id("continue")).Click();
            Assert.Contains("There are no associated permissions to the API key being requested", s.Driver.PageSource);
        }

        async Task TestApiAgainstAccessToken(string accessToken, ServerTester tester, TestAccount testAccount,
            params string[] expectedPermissionsArr)
        {
            var expectedPermissions = Permission.ToPermissions(expectedPermissionsArr).ToArray();
            var apikeydata = await TestApiAgainstAccessToken<ApiKeyData>(accessToken, $"api/v1/api-keys/current", tester.PayTester.HttpClient);
            var permissions = apikeydata.Permissions;
            TestLogs.LogInformation($"TestApiAgainstAccessToken: Permissions {permissions.Length}");
            Assert.Equal(expectedPermissions.Length, permissions.Length);
            foreach (var expectPermission in expectedPermissions)
            {
                Assert.True(permissions.Any(p => p == expectPermission), $"Missing expected permission {expectPermission}");
            }

            TestLogs.LogInformation("Testing CanViewProfile");
            if (permissions.Contains(Permission.Create(Policies.CanViewProfile)))
            {
                var resultUser = await TestApiAgainstAccessToken<string>(accessToken, $"{TestApiPath}/me/id", tester.PayTester.HttpClient);
                Assert.Equal(testAccount.UserId, resultUser);
            }
            else
            {
                await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                {
                    await TestApiAgainstAccessToken<string>(accessToken, $"{TestApiPath}/me/id", tester.PayTester.HttpClient);
                });
            }
            //create a second user to see if any of its data gets messed up in our results.
            TestLogs.LogInformation("Testing second user");
            var secondUser = tester.NewAccount();
            await secondUser.GrantAccessAsync();

            var canModifyAllStores = Permission.Create(Policies.CanModifyStoreSettings);
            var canModifyServer = Permission.Create(Policies.CanModifyServerSettings);
            var unrestricted = Permission.Create(Policies.Unrestricted);
            var selectiveStorePermissions = permissions.Where(p => p.Scope != null && p.Policy == Policies.CanModifyStoreSettings);

            TestLogs.LogInformation("Testing can edit store for first user");
            IEnumerable<Permission> storePermissions = selectiveStorePermissions as Permission[] ?? selectiveStorePermissions.ToArray();

            if (permissions.Contains(canModifyAllStores) || storePermissions.Any())
            {
                var resultStores =
                    await TestApiAgainstAccessToken<Client.Models.StoreData[]>(accessToken, $"{TestApiPath}/me/stores",
                        tester.PayTester.HttpClient);

                foreach (var selectiveStorePermission in storePermissions)
                {
                    Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{selectiveStorePermission.Scope}/can-edit",
                        tester.PayTester.HttpClient));

                    Assert.Contains(resultStores,
                        data => data.Id.Equals(selectiveStorePermission.Scope, StringComparison.InvariantCultureIgnoreCase));
                }

                bool shouldBeAuthorized = false;
                if (permissions.Contains(canModifyAllStores) || storePermissions.Contains(Permission.Create(Policies.CanViewStoreSettings, testAccount.StoreId)))
                {
                    Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{testAccount.StoreId}/can-view",
                        tester.PayTester.HttpClient));
                    Assert.Contains(resultStores,
                        data => data.Id.Equals(testAccount.StoreId, StringComparison.InvariantCultureIgnoreCase));
                    shouldBeAuthorized = true;
                }
                if (permissions.Contains(canModifyAllStores) || storePermissions.Contains(Permission.Create(Policies.CanModifyStoreSettings, testAccount.StoreId)))
                {
                    Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{testAccount.StoreId}/can-view",
                        tester.PayTester.HttpClient));
                    Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{testAccount.StoreId}/can-edit",
                        tester.PayTester.HttpClient));
                    Assert.Contains(resultStores,
                        data => data.Id.Equals(testAccount.StoreId, StringComparison.InvariantCultureIgnoreCase));
                    shouldBeAuthorized = true;
                }

                if (!shouldBeAuthorized)
                {
                    await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                    {
                        await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{testAccount.StoreId}/can-edit",
                        tester.PayTester.HttpClient);
                    });
                    await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                    {
                        await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{testAccount.StoreId}/can-view",
                        tester.PayTester.HttpClient);
                    });
                    Assert.DoesNotContain(resultStores,
                        data => data.Id.Equals(testAccount.StoreId, StringComparison.InvariantCultureIgnoreCase));
                }
            }
            else if (!permissions.Contains(unrestricted))
            {
                await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                {
                    await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{testAccount.StoreId}/can-edit",
                        tester.PayTester.HttpClient);
                });
            }
            else
            {
                await TestApiAgainstAccessToken<bool>(accessToken,
                    $"{TestApiPath}/me/stores/{testAccount.StoreId}/can-edit",
                    tester.PayTester.HttpClient);
            }

            TestLogs.LogInformation("Testing can edit store for second user");
            if (!permissions.Contains(unrestricted))
            {
                await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                {
                    await TestApiAgainstAccessToken<bool>(accessToken, $"{TestApiPath}/me/stores/{secondUser.StoreId}/can-edit",
                        tester.PayTester.HttpClient);
                });
            }
            else
            {
                await TestApiAgainstAccessToken<bool>(accessToken, $"{TestApiPath}/me/stores/{secondUser.StoreId}/can-edit",
                    tester.PayTester.HttpClient);
            }
            TestLogs.LogInformation("Testing can edit store for second user expectation met");

            TestLogs.LogInformation($"Testing CanModifyServer with {permissions.Contains(canModifyServer)}");
            if (permissions.Contains(canModifyServer))
            {
                Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                    $"{TestApiPath}/me/is-admin",
                    tester.PayTester.HttpClient));
            }
            else
            {
                await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                {
                    await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/is-admin",
                        tester.PayTester.HttpClient);
                });
            }
            TestLogs.LogInformation("Testing CanModifyServer expectation met");
        }

        private async Task<T> TestApiAgainstAccessToken<T>(string apikey, string url, HttpClient client)
        {
            var uri = new Uri(client.BaseAddress, url);
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("token", apikey);
            TestLogs.LogInformation($"Testing {uri}");
            var result = await client.SendAsync(httpRequest);
            TestLogs.LogInformation($"Testing {uri} status: {result.StatusCode}");
            result.EnsureSuccessStatusCode();

            var rawJson = await result.Content.ReadAsStringAsync();
            TestLogs.LogInformation($"Testing {uri} result: {rawJson}");
            if (typeof(T).IsPrimitive || typeof(T) == typeof(string))
            {
                return (T)Convert.ChangeType(rawJson, typeof(T));
            }

            return JsonConvert.DeserializeObject<T>(rawJson);
        }

        private string GetAccessTokenFromCallbackResult(IWebDriver driver)
        {
            var source = driver.FindElement(By.TagName("body")).Text;
            var json = JObject.Parse(source);
            return json.GetValue("apiKey")!.Value<string>();
        }
    }
}
