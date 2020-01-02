using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Security.APIKeys;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Manage;
using ExchangeSharp;
using Newtonsoft.Json;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class ApiKeysTests
    {
        public const int TestTimeout = TestUtils.TestTimeout;

        public const string TestApiPath = "{TestApiPath}/apikey";
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
                await TestApiAgainstAccessToken(superApiKey, tester, user, APIKeyConstants.Permissions.ServerManagement,
                    APIKeyConstants.Permissions.StoreManagement);


                s.Driver.FindElement(By.Id("AddApiKey")).Click();
                s.SetCheckbox(s, "ServerManagementPermission", true);
                s.Driver.FindElement(By.Id("Generate")).Click();
                var serverOnlyApiKey = s.AssertHappyMessage().FindElement(By.TagName("code")).Text;
                await TestApiAgainstAccessToken(serverOnlyApiKey, tester, user,
                    APIKeyConstants.Permissions.ServerManagement);


                s.Driver.FindElement(By.Id("AddApiKey")).Click();
                s.SetCheckbox(s, "StoreManagementPermission", true);
                s.Driver.FindElement(By.Id("Generate")).Click();
                var allStoreOnlyApiKey = s.AssertHappyMessage().FindElement(By.TagName("code")).Text;
                await TestApiAgainstAccessToken(allStoreOnlyApiKey, tester, user,
                    APIKeyConstants.Permissions.StoreManagement);

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
                    APIKeyConstants.Permissions.GetStorePermission(storeId));

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
                UriBuilder authorize = new UriBuilder(tester.PayTester.ServerUri);
                authorize.Path = "api-keys/authorize";

                authorize.AppendPayloadToQuery(new Dictionary<string, object>()
                {
                    {"redirect", "https://local.local/callback"},
                    {"applicationName", "kukksappname"},
                    {"strict", true},
                    {"selectiveStores", false},
                    {
                        "permissions",
                        new[]
                        {
                            APIKeyConstants.Permissions.StoreManagement,
                            APIKeyConstants.Permissions.ServerManagement
                        }
                    },
                });
                var authUrl = authorize.ToString();
                var perms = new[]
                {
                    APIKeyConstants.Permissions.StoreManagement, APIKeyConstants.Permissions.ServerManagement
                };
                authUrl = authUrl.Replace("permissions=System.String%5B%5D",
                    string.Join("&", perms.Select(s1 => $"permissions={s1}")));
                s.Driver.Navigate().GoToUrl(authUrl);
                s.Driver.PageSource.Contains("kukksappname");
                Assert.NotNull(s.Driver.FindElement(By.Id("StoreManagementPermission")).GetAttribute("readonly"));
                Assert.True(s.Driver.FindElement(By.Id("StoreManagementPermission")).Selected);
                Assert.NotNull(s.Driver.FindElement(By.Id("ServerManagementPermission")).GetAttribute("readonly"));
                Assert.True(s.Driver.FindElement(By.Id("ServerManagementPermission")).Selected);
                Assert.DoesNotContain("change-store-mode", s.Driver.PageSource);
                s.Driver.FindElement(By.Id("consent-yes")).Click();
                var url = s.Driver.Url;
                Assert.StartsWith("https://local.local/callback", url);
                IEnumerable<KeyValuePair<string, string>> results = url.Split("?").Last().Split("&")
                    .Select(s1 => new KeyValuePair<string, string>(s1.Split("=")[0], s1.Split("=")[1]));

                Assert.Equal(user.UserId, results.Single(pair => pair.Key == "user-id").Value);
                await TestApiAgainstAccessToken(results.Single(pair => pair.Key == "api-key").Value, tester, user,
                    results.Where(pair => pair.Key == "permissions").Select(pair => pair.Value).ToArray());

                authorize = new UriBuilder(tester.PayTester.ServerUri);
                authorize.Path = "api-keys/authorize";
                var appIdentifier = $"test_{Guid.NewGuid()}";
                authorize.AppendPayloadToQuery(new Dictionary<string, object>()
                {
                    {"redirect", "https://local.local/callback"},
                    {"strict", false},
                    {"applicationIdentifier", appIdentifier},
                    {"selectiveStores", true},
                    {
                        "permissions",
                        new[]
                        {
                            APIKeyConstants.Permissions.StoreManagement,
                            APIKeyConstants.Permissions.ServerManagement
                        }
                    }
                });
                authUrl = authorize.ToString();
                perms = new[]
                {
                    APIKeyConstants.Permissions.StoreManagement, APIKeyConstants.Permissions.ServerManagement
                };
                authUrl = authUrl.Replace("permissions=System.String%5B%5D",
                    string.Join("&", perms.Select(s1 => $"permissions={s1}")));
                s.Driver.Navigate().GoToUrl(authUrl);
                Assert.DoesNotContain("kukksappname", s.Driver.PageSource);

                Assert.Null(s.Driver.FindElement(By.Id("StoreManagementPermission")).GetAttribute("readonly"));
                Assert.True(s.Driver.FindElement(By.Id("StoreManagementPermission")).Selected);
                Assert.Null(s.Driver.FindElement(By.Id("ServerManagementPermission")).GetAttribute("readonly"));
                Assert.True(s.Driver.FindElement(By.Id("ServerManagementPermission")).Selected);

                s.SetCheckbox(s, "ServerManagementPermission", false);
                Assert.Contains("change-store-mode", s.Driver.PageSource);
                s.Driver.FindElement(By.Id("consent-yes")).Click();
                url = s.Driver.Url;
                Assert.StartsWith("https://local.local/callback", url);
                results = url.Split("?").Last().Split("&")
                    .Select(s1 => new KeyValuePair<string, string>(s1.Split("=")[0], s1.Split("=")[1]));

                Assert.Equal(user.UserId, results.Single(pair => pair.Key == "user-id").Value);
                await TestApiAgainstAccessToken(results.Single(pair => pair.Key == "api-key").Value, tester, user,
                    results.Where(pair => pair.Key == "permissions").Select(pair => pair.Value).ToArray());

                //let's test the app identifier system


                //if it's the same, go to redirect directly
                s.Driver.Navigate().GoToUrl(authUrl);
                url = s.Driver.Url;
                Assert.StartsWith("https://local.local/callback", url);


                //if we request authorization for app identifier "test" but with different callback url, show auth screen

                authorize = new UriBuilder(tester.PayTester.ServerUri);
                authorize.Path = "api-keys/authorize";

                authorize.AppendPayloadToQuery(new Dictionary<string, object>()
                {
                    {"redirect", "https://international.com/callback"},
                    {"strict", false},
                    {"applicationIdentifier", appIdentifier},
                    {"selectiveStores", true},
                    {
                        "permissions",
                        new[]
                        {
                            APIKeyConstants.Permissions.StoreManagement,
                            APIKeyConstants.Permissions.ServerManagement
                        }
                    }
                });
                authUrl = authorize.ToString();
                perms = new[]
                {
                    APIKeyConstants.Permissions.StoreManagement, APIKeyConstants.Permissions.ServerManagement
                };
                authUrl = authUrl.Replace("permissions=System.String%5B%5D",
                    string.Join("&", perms.Select(s1 => $"permissions={s1}")));
                s.Driver.Navigate().GoToUrl(authUrl);

                url = s.Driver.Url;
                Assert.False(url.StartsWith("https://international.com/callback"));
            }
        }

        async Task TestApiAgainstAccessToken(string accessToken, ServerTester tester, TestAccount testAccount,
            params string[] permissions)
        {
            var resultUser =
                await TestApiAgainstAccessToken<string>(accessToken, "{TestApiPath}/me/id",
                    tester.PayTester.HttpClient);
            Assert.Equal(testAccount.UserId, resultUser);

            //create a second user to see if any of its data gets messed upin our results.
            var secondUser = tester.NewAccount();
            secondUser.GrantAccess();

            var selectiveStorePermissions = APIKeyConstants.Permissions.ExtractStorePermissionsIds(permissions);
            if (permissions.Contains(APIKeyConstants.Permissions.StoreManagement) || selectiveStorePermissions.Any())
            {
                var resultStores =
                    await TestApiAgainstAccessToken<StoreData[]>(accessToken, "{TestApiPath}/me/stores",
                        tester.PayTester.HttpClient);

                foreach (string selectiveStorePermission in selectiveStorePermissions)
                {
                    Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                        $"{TestApiPath}/me/stores/{selectiveStorePermission}/can-edit",
                        tester.PayTester.HttpClient));

                    Assert.Contains(resultStores,
                        data => data.Id.Equals(selectiveStorePermission, StringComparison.InvariantCultureIgnoreCase));
                }

                if (permissions.Contains(APIKeyConstants.Permissions.StoreManagement))
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

            if (permissions.Contains(APIKeyConstants.Permissions.ServerManagement))
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
            httpRequest.Headers.Add("X-APIKEY", apikey);
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
