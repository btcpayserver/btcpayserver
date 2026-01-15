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
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

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
        [Trait("Playwright", "Playwright-2")]
        public async Task CanCreateApiKeys()
        {
            //there are 2 ways to create api keys:
            //as a user through your profile
            //as an external application requesting an api key from a user

            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            var tester = s.Server;

            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            await user.MakeAdmin(false);
            await s.GoToLogin();
            await s.LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
            await s.GoToProfile(ManageNavPages.APIKeys);
            await s.ClickPagePrimary();

            async Task AssertNoPermission(string permission)
            {
                var txt = await s.Page.ContentAsync();
                Assert.DoesNotContain(permission, txt);
            }
            async Task AssertPermission(string permission)
            {
                var txt = await s.Page.ContentAsync();
                Assert.Contains(permission, txt);
            }

            //not an admin, so this permission should not show
            await AssertNoPermission("btcpay.server.canmodifyserversettings");
            await user.MakeAdmin();
            await s.Logout();
            await s.GoToLogin();
            await s.LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
            await s.GoToProfile(ManageNavPages.APIKeys);
            await s.ClickPagePrimary();
            await AssertPermission("btcpay.server.canmodifyserversettings");

            //server management should show now
            await s.Page.SetCheckedAsync("#btcpay\\.server\\.canmodifyserversettings", true);
            await s.Page.SetCheckedAsync("#btcpay\\.store\\.canmodifystoresettings", true);
            await s.Page.SetCheckedAsync("#btcpay\\.user\\.canviewprofile", true);
            await s.ClickPagePrimary();
            var superApiKey = await (await s.FindAlertMessage()).Locator("code").TextContentAsync();

            //this api key has access to everything
            await TestApiAgainstAccessToken(superApiKey, tester, user, Policies.CanModifyServerSettings, Policies.CanModifyStoreSettings, Policies.CanViewProfile);

            await s.ClickPagePrimary();
            await s.Page.SetCheckedAsync("#btcpay\\.server\\.canmodifyserversettings", true);
            await s.ClickPagePrimary();
            var serverOnlyApiKey = await (await s.FindAlertMessage()).Locator("code").TextContentAsync();
            await TestApiAgainstAccessToken(serverOnlyApiKey, tester, user,
                Policies.CanModifyServerSettings);

            await s.ClickPagePrimary();
            await s.Page.SetCheckedAsync("#btcpay\\.store\\.canmodifystoresettings", true);
            await s.ClickPagePrimary();
            var allStoreOnlyApiKey = await (await s.FindAlertMessage()).Locator("code").TextContentAsync();
            await TestApiAgainstAccessToken(allStoreOnlyApiKey, tester, user,
                Policies.CanModifyStoreSettings);

            await s.ClickPagePrimary();
            await s.Page.Locator("button[value='btcpay.store.canmodifystoresettings:change-store-mode']").ClickAsync();
            //there should be a store already by default in the dropdown
            var getPermissionValueIndex =
                await s.Page.Locator("input[value='btcpay.store.canmodifystoresettings']")
                    .GetAttributeAsync("name");
            getPermissionValueIndex = getPermissionValueIndex!.Replace(".Permission", ".SpecificStores[0]");
            await s.Page.SelectOptionAsync($"[name='{getPermissionValueIndex}']", user.StoreId);
            await s.ClickPagePrimary();
            var selectiveStoreApiKey = await (await s.FindAlertMessage()).Locator("code").TextContentAsync();
            await TestApiAgainstAccessToken(selectiveStoreApiKey, tester, user,
                Permission.Create(Policies.CanModifyStoreSettings, user.StoreId).ToString());

            await s.ClickPagePrimary(); // New API key
            await s.ClickPagePrimary(); // Generate
            var noPermissionsApiKey = await (await s.FindAlertMessage()).Locator("code").TextContentAsync();
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
            await s.GoToUrl(authUrl);
            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var pageContent = await s.Page.ContentAsync();
            Assert.Contains(appidentifier, pageContent);
            Assert.True(await s.Page.Locator("select#StoreId").CountAsync() == 0);

            // No upfront store selection with selectiveStores being false
            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings }, selectiveStores: false, applicationDetails: (appidentifier, new Uri(callbackUrl))).ToString();
            await s.GoToUrl(authUrl);
            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            Assert.True(await s.Page.Locator("select#StoreId").CountAsync() == 0);

            // Now with store settings
            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings }, selectiveStores: true, applicationDetails: (appidentifier, new Uri(callbackUrl))).ToString();
            await s.GoToUrl(authUrl);
            var storeSettingsPageContent = await s.Page.ContentAsync();
            Assert.Contains(appidentifier, storeSettingsPageContent);

            // Select a store
            await s.Page.SelectOptionAsync("#StoreId", user.StoreId);
            await s.Page.Locator("#continue").ClickAsync();

            Assert.Equal("hidden", (await s.Page.GetAttributeAsync("#btcpay\\.store\\.canmodifystoresettings", "type")).ToLowerInvariant());
            Assert.Equal("true", (await s.Page.GetAttributeAsync("#btcpay\\.store\\.canmodifystoresettings", "value")).ToLowerInvariant());
            Assert.Equal("hidden", (await s.Page.GetAttributeAsync("#btcpay\\.server\\.canmodifyserversettings", "type")).ToLowerInvariant());
            Assert.Equal("true", (await s.Page.GetAttributeAsync("#btcpay\\.server\\.canmodifyserversettings", "value")).ToLowerInvariant());
            var pageContent2 = await s.Page.ContentAsync();
            Assert.DoesNotContain("change-store-mode", pageContent2);

            await s.Page.Locator("#consent-yes").ClickAsync();
            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            Assert.Equal(callbackUrl, s.Page.Url);

            var apiKeyRepo = s.Server.PayTester.GetService<APIKeyRepository>();
            var accessToken = await GetAccessTokenFromCallbackResult(s);
            await TestApiAgainstAccessToken(accessToken, tester, user,
                (await apiKeyRepo.GetKey(accessToken)).GetBlob().Permissions);

            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings }, false, true, (null, new Uri(callbackUrl))).ToString();

            await s.GoToUrl(authUrl);
            var kukksappPageContent = await s.Page.ContentAsync();
            Assert.DoesNotContain("kukksappname", kukksappPageContent);

            // Select a store
            await s.Page.SelectOptionAsync("#StoreId", user.StoreId);
            await s.Page.Locator("#continue").ClickAsync();

            Assert.Equal("checkbox", (await s.Page.GetAttributeAsync("#btcpay\\.store\\.canmodifystoresettings", "type")).ToLowerInvariant());
            Assert.Equal("true", (await s.Page.GetAttributeAsync("#btcpay\\.store\\.canmodifystoresettings", "value")).ToLowerInvariant());
            Assert.Equal("checkbox", (await s.Page.GetAttributeAsync("#btcpay\\.server\\.canmodifyserversettings", "type")).ToLowerInvariant());
            Assert.Equal("true", (await s.Page.GetAttributeAsync("#btcpay\\.server\\.canmodifyserversettings", "value")).ToLowerInvariant());

            await s.Page.SetCheckedAsync("#btcpay\\.server\\.canmodifyserversettings", false);
            await s.Page.Locator("#consent-yes").ClickAsync();
            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            Assert.Equal(callbackUrl, s.Page.Url);

            accessToken = await GetAccessTokenFromCallbackResult(s);
            await TestApiAgainstAccessToken(accessToken, tester, user,
                (await apiKeyRepo.GetKey(accessToken)).GetBlob().Permissions);

            //let's test the app identifier system
            TestLogs.LogInformation("Checking app identifier system");
            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings }, false, true, (appidentifier, new Uri(callbackUrl))).ToString();

            //if it's the same, go to the confirm page
            await s.GoToUrl(authUrl);

            // Select the same store
            await s.Page.SelectOptionAsync("#StoreId", user.StoreId);
            await s.Page.Locator("#continue").ClickAsync();

            var confirmPageContent = await s.Page.ContentAsync();
            Assert.Contains("previously generated the API Key", confirmPageContent);
            await s.Page.Locator("#continue").ClickAsync();
            await s.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            Assert.Equal(callbackUrl, s.Page.Url);

            //same app but different redirect = nono
            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri,
                new[] { Policies.CanModifyStoreSettings, Policies.CanModifyServerSettings }, false, true, (appidentifier, new Uri("https://international.local/callback"))).ToString();

            await s.GoToUrl(authUrl);

            // Select the same store
            await s.Page.SelectOptionAsync("#StoreId", user.StoreId);
            await s.Page.Locator("#continue").ClickAsync();

            var internationalPageContent = await s.Page.ContentAsync();
            Assert.DoesNotContain("previously generated the API Key", internationalPageContent);
            Assert.False(s.Page.Url.StartsWith("https://international.local/callback"));

            // Make sure we can check all permissions when not an admin
            TestLogs.LogInformation("Make sure we can check all permissions when not an admin");
            await user.MakeAdmin(false);
            await s.GoToHome();
            await s.Logout();
            await s.GoToLogin();
            await s.LogIn(user.RegisterDetails.Email, user.RegisterDetails.Password);
            await s.GoToUrl("/account/apikeys");
            await s.ClickPagePrimary();
            int checkedPermissionCount = await s.Page.Locator(".form-check-input").CountAsync();
            await s.Page.EvaluateAsync("document.querySelectorAll('#Permissions .form-check-input').forEach(i => i.click())");

            TestLogs.LogInformation("Generating API key");
            await s.ClickPagePrimary();
            var allAPIKey = await (await s.FindAlertMessage()).Locator("code").TextContentAsync();

            TestLogs.LogInformation($"Checking API key permissions: {allAPIKey}");
            var apikeydata = await TestApiAgainstAccessToken<ApiKeyData>(allAPIKey, "api/v1/api-keys/current", tester.PayTester.HttpClient);
            Assert.Equal(checkedPermissionCount, apikeydata.Permissions.Length);

            TestLogs.LogInformation("Checking empty permissions");
            authUrl = BTCPayServerClient.GenerateAuthorizeUri(s.ServerUri, Array.Empty<string>(), false, true).ToString();
            await s.GoToUrl(authUrl);
            await s.Page.SelectOptionAsync("#StoreId", user.StoreId);
            await s.Page.Locator("#continue").ClickAsync();
            var emptyPageContent = await s.Page.ContentAsync();
            Assert.Contains("There are no associated permissions to the API key being requested", emptyPageContent);
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

        private async Task<string> GetAccessTokenFromCallbackResult(PlaywrightTester tester)
        {
            var source = await tester.Page.Locator("body").TextContentAsync();
            var json = JObject.Parse(source);
            return json.GetValue("apiKey")!.Value<string>();
        }
    }
}
