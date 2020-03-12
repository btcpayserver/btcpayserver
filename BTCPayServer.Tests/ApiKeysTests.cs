using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Security.APIKeys;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Manage;
using Newtonsoft.Json;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class ApiKeysTests
    {
        public const int TestTimeout = TestUtils.TestTimeout;

        public const string TestApiPath = "api/test/apikey";
        public ApiKeysTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Selenium", "Selenium")]
        public async Task CanCreateApiKeys()
        {
            //there are 2 ways to create api keys:
            //as a user through your profile
            //as an external application requesting an api key from a user

            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                var tester = s.Server;

                var user = tester.NewAccount();
                user.GrantAccess();

                await user.CreateStoreAsync();
                s.GoToLogin();
                s.Login(user.RegisterDetails.Email, user.RegisterDetails.Password);
                s.GoToProfile(ManageNavPages.APIKeys);
                s.Driver.FindElement(By.Id("AddApiKey")).Click();
                if (!user.IsAdmin)
                {
                    //not an admin, so this permission should not show
                    Assert.DoesNotContain("ServerManagementPermission", s.Driver.PageSource);
                    await user.MakeAdmin();
                    s.Logout();
                    s.GoToLogin();
                    s.Login(user.RegisterDetails.Email, user.RegisterDetails.Password);
                    s.GoToProfile(ManageNavPages.APIKeys);
                    s.Driver.FindElement(By.Id("AddApiKey")).Click();
                }

                //server management should show now
                s.SetCheckbox(s, "ServerManagementPermission", true);
                s.SetCheckbox(s, "StoreManagementPermission", true);
                s.Driver.FindElement(By.Id("Generate")).Click();
                var superApiKey = s.AssertHappyMessage().FindElement(By.TagName("code")).Text;

                //this api key has access to everything
                await TestApiAgainstAccessToken(superApiKey, tester, user, Permissions.ServerManagement,
                    Permissions.StoreManagement);


                s.Driver.FindElement(By.Id("AddApiKey")).Click();
                s.SetCheckbox(s, "ServerManagementPermission", true);
                s.Driver.FindElement(By.Id("Generate")).Click();
                var serverOnlyApiKey = s.AssertHappyMessage().FindElement(By.TagName("code")).Text;
                await TestApiAgainstAccessToken(serverOnlyApiKey, tester, user,
                    Permissions.ServerManagement);


                s.Driver.FindElement(By.Id("AddApiKey")).Click();
                s.SetCheckbox(s, "StoreManagementPermission", true);
                s.Driver.FindElement(By.Id("Generate")).Click();
                var allStoreOnlyApiKey = s.AssertHappyMessage().FindElement(By.TagName("code")).Text;
                await TestApiAgainstAccessToken(allStoreOnlyApiKey, tester, user,
                    Permissions.StoreManagement);

                s.Driver.FindElement(By.Id("AddApiKey")).Click();
                s.Driver.FindElement(By.CssSelector("button[value=change-store-mode]")).Click();
                //there should be a store already by default in the dropdown
                var dropdown = s.Driver.FindElement(By.Name("SpecificStores[0]"));
                var option = dropdown.FindElement(By.TagName("option"));
                var storeId = option.GetAttribute("value");
                option.Click();
                s.Driver.FindElement(By.Id("Generate")).Click();
                var selectiveStoreApiKey = s.AssertHappyMessage().FindElement(By.TagName("code")).Text;
                await TestApiAgainstAccessToken(selectiveStoreApiKey, tester, user,
                    Permissions.GetStorePermission(storeId));

                s.Driver.FindElement(By.Id("AddApiKey")).Click();
                s.Driver.FindElement(By.Id("Generate")).Click();
                var noPermissionsApiKey = s.AssertHappyMessage().FindElement(By.TagName("code")).Text;
                await TestApiAgainstAccessToken(noPermissionsApiKey, tester, user);

                await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                {
                    await TestApiAgainstAccessToken<bool>("incorrect key", $"{TestApiPath}/me/id",
                        tester.PayTester.HttpClient);
                });


                //let's test the authorized screen now
                //options for authorize are:
                //applicationName
                //redirect
                //permissions
                //strict
                //selectiveStores
                var authUrl = BTCPayServerClient.GenerateAuthorizeUri(tester.PayTester.ServerUri,
                    new[] {Permissions.StoreManagement, Permissions.ServerManagement}).ToString();
                s.Driver.Navigate().GoToUrl(authUrl);
                s.Driver.PageSource.Contains("kukksappname");
                Assert.Equal("hidden", s.Driver.FindElement(By.Id("StoreManagementPermission")).GetAttribute("type").ToLowerInvariant());
                Assert.Equal("true", s.Driver.FindElement(By.Id("StoreManagementPermission")).GetAttribute("value").ToLowerInvariant());
                Assert.Equal("hidden", s.Driver.FindElement(By.Id("ServerManagementPermission")).GetAttribute("type").ToLowerInvariant());
                Assert.Equal("true",s.Driver.FindElement(By.Id("ServerManagementPermission")).GetAttribute("value").ToLowerInvariant());
                Assert.DoesNotContain("change-store-mode", s.Driver.PageSource);
                s.Driver.FindElement(By.Id("consent-yes")).Click();
                var url = s.Driver.Url;
                IEnumerable<KeyValuePair<string, string>> results = url.Split("?").Last().Split("&")
                    .Select(s1 => new KeyValuePair<string, string>(s1.Split("=")[0], s1.Split("=")[1]));

                var apiKeyRepo = s.Server.PayTester.GetService<APIKeyRepository>();
               
                await TestApiAgainstAccessToken(results.Single(pair => pair.Key == "key").Value, tester, user,
                    (await apiKeyRepo.GetKey(results.Single(pair => pair.Key == "key").Value)).GetPermissions());

                authUrl = BTCPayServerClient.GenerateAuthorizeUri(tester.PayTester.ServerUri,
                    new[] {Permissions.StoreManagement, Permissions.ServerManagement}, false, true).ToString();
                
                s.Driver.Navigate().GoToUrl(authUrl);
                Assert.DoesNotContain("kukksappname", s.Driver.PageSource);

                Assert.Equal("checkbox", s.Driver.FindElement(By.Id("StoreManagementPermission")).GetAttribute("type").ToLowerInvariant());
                Assert.Equal("true", s.Driver.FindElement(By.Id("StoreManagementPermission")).GetAttribute("value").ToLowerInvariant());
                Assert.Equal("checkbox", s.Driver.FindElement(By.Id("ServerManagementPermission")).GetAttribute("type").ToLowerInvariant());
                Assert.Equal("true",s.Driver.FindElement(By.Id("ServerManagementPermission")).GetAttribute("value").ToLowerInvariant());

                s.SetCheckbox(s, "ServerManagementPermission", false);
                Assert.Contains("change-store-mode", s.Driver.PageSource);
                s.Driver.FindElement(By.Id("consent-yes")).Click();
                url = s.Driver.Url;
                results = url.Split("?").Last().Split("&")
                    .Select(s1 => new KeyValuePair<string, string>(s1.Split("=")[0], s1.Split("=")[1]));
                
                await TestApiAgainstAccessToken(results.Single(pair => pair.Key == "key").Value, tester, user,
                    (await apiKeyRepo.GetKey(results.Single(pair => pair.Key == "key").Value)).GetPermissions());
                
            }
        }

        async Task TestApiAgainstAccessToken(string accessToken, ServerTester tester, TestAccount testAccount,
            params string[] permissions)
        {
            var resultUser =
                await TestApiAgainstAccessToken<string>(accessToken, $"{TestApiPath}/me/id",
                    tester.PayTester.HttpClient);
            Assert.Equal(testAccount.UserId, resultUser);

            //create a second user to see if any of its data gets messed upin our results.
            var secondUser = tester.NewAccount();
            secondUser.GrantAccess();

            var selectiveStorePermissions = Permissions.ExtractStorePermissionsIds(permissions);
            if (permissions.Contains(Permissions.StoreManagement) || selectiveStorePermissions.Any())
            {
                var resultStores =
                    await TestApiAgainstAccessToken<StoreData[]>(accessToken, $"{TestApiPath}/me/stores",
                        tester.PayTester.HttpClient);

                foreach (string selectiveStorePermission in selectiveStorePermissions)
                {
                    Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{selectiveStorePermission}/can-edit",
                        tester.PayTester.HttpClient));

                    Assert.Contains(resultStores,
                        data => data.Id.Equals(selectiveStorePermission, StringComparison.InvariantCultureIgnoreCase));
                }

                if (permissions.Contains(Permissions.StoreManagement))
                {
                    Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/actions",
                        tester.PayTester.HttpClient));

                    Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{testAccount.StoreId}/can-edit",
                        tester.PayTester.HttpClient));
                    Assert.Contains(resultStores,
                        data => data.Id.Equals(testAccount.StoreId, StringComparison.InvariantCultureIgnoreCase));
                }
                else
                {
                    await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                    {
                        await TestApiAgainstAccessToken<bool>(accessToken,
                            $"{TestApiPath}/me/stores/actions",
                            tester.PayTester.HttpClient);
                    });
                }

                Assert.DoesNotContain(resultStores,
                    data => data.Id.Equals(secondUser.StoreId, StringComparison.InvariantCultureIgnoreCase));
            }
            else
            {
                await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                {
                    await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{testAccount.StoreId}/can-edit",
                        tester.PayTester.HttpClient);
                });
            }

            await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
            {
                await TestApiAgainstAccessToken<bool>(accessToken, $"{TestApiPath}/me/stores/{secondUser.StoreId}/can-edit",
                    tester.PayTester.HttpClient);
            });

            if (permissions.Contains(Permissions.ServerManagement))
            {
                Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                    $"{TestApiPath}/me/is-admin",
                    tester.PayTester.HttpClient));
            }
        }

        public async Task<T> TestApiAgainstAccessToken<T>(string apikey, string url, HttpClient client)
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Get,
                new Uri(client.BaseAddress, url));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("token", apikey);
            var result = await client.SendAsync(httpRequest);
            result.EnsureSuccessStatusCode();

            var rawJson = await result.Content.ReadAsStringAsync();
            if (typeof(T).IsPrimitive || typeof(T) == typeof(string))
            {
                return (T)Convert.ChangeType(rawJson, typeof(T));
            }

            return JsonConvert.DeserializeObject<T>(rawJson);
        }
    }
}
