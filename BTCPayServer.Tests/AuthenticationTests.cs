using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Security.Claims;
using BTCPayServer.Tests.Logging;
using Xunit;
using Xunit.Abstractions;
using System.Net.Http;
using System.Net.Http.Headers;
using BTCPayServer.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenIddict.Abstractions;
using OpenQA.Selenium;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.Tests
{
    public class AuthenticationTests
    {
        public const int TestTimeout = TestUtils.TestTimeout;
        public AuthenticationTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task GetRedirectedToLoginPathOnChallenge()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var client = tester.PayTester.HttpClient;
                //Wallets endpoint is protected
                var response = await client.GetAsync("wallets");
                var urlPath = response.RequestMessage.RequestUri.ToString()
                    .Replace(tester.PayTester.ServerUri.ToString(), "");
                //Cookie Challenge redirects you to login page
                Assert.StartsWith("Account/Login", urlPath, StringComparison.InvariantCultureIgnoreCase);

                var queryString = response.RequestMessage.RequestUri.ParseQueryString();

                Assert.NotNull(queryString["ReturnUrl"]);
                Assert.Equal("/wallets", queryString["ReturnUrl"]);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanGetOpenIdConfiguration()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                using (var response =
                    await tester.PayTester.HttpClient.GetAsync("/.well-known/openid-configuration"))
                {
                    using (var streamToReadFrom = new StreamReader(await response.Content.ReadAsStreamAsync()))
                    {
                        var json = await streamToReadFrom.ReadToEndAsync();
                        Assert.NotNull(json);
                        JObject.Parse(json); // Should do more tests but good enough
                    }
                }
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseNonInteractiveFlows()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();

                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                var token = await RegisterPasswordClientAndGetAccessToken(user, null, tester);
                await TestApiAgainstAccessToken(token, tester, user);
                token = await RegisterPasswordClientAndGetAccessToken(user, "secret", tester);
                await TestApiAgainstAccessToken(token, tester, user);
                token = await RegisterClientCredentialsFlowAndGetAccessToken(user, "secret", tester);
                await TestApiAgainstAccessToken(token, tester, user);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Selenium", "Selenium")]
        public async Task CanUseImplicitFlow()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                var tester = s.Server;

                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                var id = Guid.NewGuid().ToString();
                var redirecturi = new Uri("http://127.0.0.1/oidc-callback");
                var openIdClient = await user.RegisterOpenIdClient(
                    new OpenIddictApplicationDescriptor()
                    {
                        ClientId = id,
                        DisplayName = id,
                        Permissions = {OpenIddictConstants.Permissions.GrantTypes.Implicit},
                        RedirectUris = {redirecturi},
                        
                    });
                var implicitAuthorizeUrl = new Uri(tester.PayTester.ServerUri,
                    $"connect/authorize?response_type=token&client_id={id}&redirect_uri={redirecturi.AbsoluteUri}&scope=openid server_management store_management&nonce={Guid.NewGuid().ToString()}");
                s.Driver.Navigate().GoToUrl(implicitAuthorizeUrl);
                s.Login(user.RegisterDetails.Email, user.RegisterDetails.Password);
                s.Driver.FindElement(By.Id("consent-yes")).Click();
                var url = s.Driver.Url;
                var results = url.Split("#").Last().Split("&")
                    .ToDictionary(s1 => s1.Split("=")[0], s1 => s1.Split("=")[1]);
                await TestApiAgainstAccessToken(results["access_token"], tester, user);
                //in Implicit mode, you renew your token  by hitting the same endpoint but adding prompt=none. If you are still logged in on the site, you will receive a fresh token.
                var implicitAuthorizeUrlSilentModel = new Uri($"{implicitAuthorizeUrl.OriginalString}&prompt=none");
                s.Driver.Navigate().GoToUrl(implicitAuthorizeUrlSilentModel);
                url = s.Driver.Url;
                results = url.Split("#").Last().Split("&").ToDictionary(s1 => s1.Split("=")[0], s1 => s1.Split("=")[1]);
                await TestApiAgainstAccessToken(results["access_token"], tester, user);

                var stores = await TestApiAgainstAccessToken<StoreData[]>(results["access_token"],
                    $"api/test/me/stores",
                    tester.PayTester.HttpClient);
                Assert.NotEmpty(stores);

                Assert.True(await TestApiAgainstAccessToken<bool>(results["access_token"],
                $"api/test/me/stores/{stores[0].Id}/can-edit",
                tester.PayTester.HttpClient));

                //we dont ask for consent after acquiring it the first time for the same scopes.
                LogoutFlow(tester, id, s);
                s.Driver.Navigate().GoToUrl(implicitAuthorizeUrl);
                s.Login(user.RegisterDetails.Email, user.RegisterDetails.Password);
                s.Driver.AssertElementNotFound(By.Id("consent-yes"));

                // Let's asks without scopes
                LogoutFlow(tester, id, s);
                id = Guid.NewGuid().ToString();
                openIdClient = await user.RegisterOpenIdClient(
                    new OpenIddictApplicationDescriptor()
                    {
                        ClientId = id,
                        DisplayName = id,
                        Permissions = { OpenIddictConstants.Permissions.GrantTypes.Implicit },
                        RedirectUris = { redirecturi },
                    });
                implicitAuthorizeUrl = new Uri(tester.PayTester.ServerUri,
                    $"connect/authorize?response_type=token&client_id={id}&redirect_uri={redirecturi.AbsoluteUri}&scope=openid&nonce={Guid.NewGuid().ToString()}");
                s.Driver.Navigate().GoToUrl(implicitAuthorizeUrl);
                s.Login(user.RegisterDetails.Email, user.RegisterDetails.Password);
                s.Driver.FindElement(By.Id("consent-yes")).Click();
                results = s.Driver.Url.Split("#").Last().Split("&")
                    .ToDictionary(s1 => s1.Split("=")[0], s1 => s1.Split("=")[1]);

                await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                {
                    await TestApiAgainstAccessToken<StoreData[]>(results["access_token"],
                    $"api/test/me/stores",
                    tester.PayTester.HttpClient);
                });
                await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
                {
                    await TestApiAgainstAccessToken<bool>(results["access_token"],
                    $"api/test/me/stores/{stores[0].Id}/can-edit",
                    tester.PayTester.HttpClient);
                });
            }
        }

        void LogoutFlow(ServerTester tester, string clientId, SeleniumTester seleniumTester)
        {
            var logoutUrl = new Uri(tester.PayTester.ServerUri,
                $"connect/logout?response_type=token&client_id={clientId}");
            seleniumTester.Driver.Navigate().GoToUrl(logoutUrl);
            seleniumTester.GoToHome();
            Assert.Throws<NoSuchElementException>(() => seleniumTester.Driver.FindElement(By.Id("Logout")));
            
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Selenium", "Selenium")]
        public async Task CanUseCodeFlow()
        {
            using (var s = SeleniumTester.Create())
            {
                await s.StartAsync();
                var tester = s.Server;

                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                var id = Guid.NewGuid().ToString();
                var redirecturi = new Uri("http://127.0.0.1/oidc-callback");
                var secret = "secret";
                var openIdClient = await user.RegisterOpenIdClient(
                    new OpenIddictApplicationDescriptor()
                    {
                        ClientId = id,
                        DisplayName = id,
                        Permissions =
                        {
                            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                            OpenIddictConstants.Permissions.GrantTypes.RefreshToken
                        },
                        RedirectUris = {redirecturi}
                    }, secret);
                var authorizeUrl = new Uri(tester.PayTester.ServerUri,
                    $"connect/authorize?response_type=code&client_id={id}&redirect_uri={redirecturi.AbsoluteUri}&scope=openid offline_access server_management store_management&state={Guid.NewGuid().ToString()}");
                s.Driver.Navigate().GoToUrl(authorizeUrl);
                s.Login(user.RegisterDetails.Email, user.RegisterDetails.Password);
                s.Driver.FindElement(By.Id("consent-yes")).Click();
                var url = s.Driver.Url;
                var results = url.Split("?").Last().Split("&")
                    .ToDictionary(s1 => s1.Split("=")[0], s1 => s1.Split("=")[1]);

                var httpClient = tester.PayTester.HttpClient;

                var httpRequest = new HttpRequestMessage(HttpMethod.Post,
                    new Uri(tester.PayTester.ServerUri, "/connect/token"))
                {
                    Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
                    {
                        new KeyValuePair<string, string>("grant_type",
                            OpenIddictConstants.GrantTypes.AuthorizationCode),
                        new KeyValuePair<string, string>("client_id", openIdClient.ClientId),
                        new KeyValuePair<string, string>("client_secret", secret),
                        new KeyValuePair<string, string>("code", results["code"]),
                        new KeyValuePair<string, string>("redirect_uri", redirecturi.AbsoluteUri)
                    })
                };


                var response = await httpClient.SendAsync(httpRequest);

                Assert.True(response.IsSuccessStatusCode);

                string content = await response.Content.ReadAsStringAsync();
                var result = System.Text.Json.JsonSerializer.Deserialize<OpenIddictResponse>(content);

                await TestApiAgainstAccessToken(result.AccessToken, tester, user);

                var refreshedAccessToken = await RefreshAnAccessToken(result.RefreshToken, httpClient, id, secret);

                await TestApiAgainstAccessToken(refreshedAccessToken, tester, user);
                
                LogoutFlow(tester, id, s);
                s.Driver.Navigate().GoToUrl(authorizeUrl);
                s.Login(user.RegisterDetails.Email, user.RegisterDetails.Password);
                
                Assert.Throws<NoSuchElementException>(() => s.Driver.FindElement(By.Id("consent-yes")));
                results = url.Split("?").Last().Split("&")
                    .ToDictionary(s1 => s1.Split("=")[0], s1 => s1.Split("=")[1]);
                Assert.True(results.ContainsKey("code"));
            }
        }

        private static async Task<string> RefreshAnAccessToken(string refreshToken, HttpClient client, string clientId,
            string clientSecret = null)
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post,
                new Uri(client.BaseAddress, "/connect/token"))
            {
                Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("grant_type",
                        OpenIddictConstants.GrantTypes.RefreshToken),
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("client_secret", clientSecret),
                    new KeyValuePair<string, string>("refresh_token", refreshToken)
                })
            };

            var response = await client.SendAsync(httpRequest);

            Assert.True(response.IsSuccessStatusCode);

            string content = await response.Content.ReadAsStringAsync();
            var result = System.Text.Json.JsonSerializer.Deserialize<OpenIddictResponse>(content);
            Assert.NotEmpty(result.AccessToken);
            Assert.Null(result.Error);
            return result.AccessToken;
        }

        private static async Task<string> RegisterClientCredentialsFlowAndGetAccessToken(TestAccount user,
            string secret,
            ServerTester tester)
        {
            var id = Guid.NewGuid().ToString();
            var openIdClient = await user.RegisterOpenIdClient(
                new OpenIddictApplicationDescriptor()
                {
                    ClientId = id,
                    DisplayName = id,
                    Permissions = {OpenIddictConstants.Permissions.GrantTypes.ClientCredentials}
                }, secret);


            var httpClient = tester.PayTester.HttpClient;

            var httpRequest = new HttpRequestMessage(HttpMethod.Post,
                new Uri(tester.PayTester.ServerUri, "/connect/token"))
            {
                Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("grant_type",
                        OpenIddictConstants.GrantTypes.ClientCredentials),
                    new KeyValuePair<string, string>("client_id", openIdClient.ClientId),
                    new KeyValuePair<string, string>("client_secret", secret),
                    new KeyValuePair<string, string>("scope", "server_management store_management")
                })
            };


            var response = await httpClient.SendAsync(httpRequest);

            Assert.True(response.IsSuccessStatusCode);

            string content = await response.Content.ReadAsStringAsync();
            var result = System.Text.Json.JsonSerializer.Deserialize<OpenIddictResponse>(content);
            Assert.NotEmpty(result.AccessToken);
            Assert.Null(result.Error);
            return result.AccessToken;
        }

        private static async Task<string> RegisterPasswordClientAndGetAccessToken(TestAccount user, string secret,
            ServerTester tester)
        {
            var id = Guid.NewGuid().ToString();
            var openIdClient = await user.RegisterOpenIdClient(
                new OpenIddictApplicationDescriptor()
                {
                    ClientId = id,
                    DisplayName = id,
                    Permissions = {OpenIddictConstants.Permissions.GrantTypes.Password}
                }, secret);


            var httpClient = tester.PayTester.HttpClient;

            var httpRequest = new HttpRequestMessage(HttpMethod.Post,
                new Uri(tester.PayTester.ServerUri, "/connect/token"))
            {
                Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("grant_type", OpenIddictConstants.GrantTypes.Password),
                    new KeyValuePair<string, string>("username", user.RegisterDetails.Email),
                    new KeyValuePair<string, string>("password", user.RegisterDetails.Password),
                    new KeyValuePair<string, string>("client_id", openIdClient.ClientId),
                    new KeyValuePair<string, string>("client_secret", secret),
                    new KeyValuePair<string, string>("scope", "server_management store_management")
                })
            };


            var response = await httpClient.SendAsync(httpRequest);

            Assert.True(response.IsSuccessStatusCode);

            string content = await response.Content.ReadAsStringAsync();
            var result = System.Text.Json.JsonSerializer.Deserialize<OpenIddictResponse>(content);
            Assert.NotEmpty(result.AccessToken);
            Assert.Null(result.Error);
            return result.AccessToken;
        }

        async Task TestApiAgainstAccessToken(string accessToken, ServerTester tester, TestAccount testAccount)
        {
            var resultUser =
                await TestApiAgainstAccessToken<string>(accessToken, "api/test/me/id",
                    tester.PayTester.HttpClient);
            Assert.Equal(testAccount.UserId, resultUser);

            var secondUser = tester.NewAccount();
            secondUser.GrantAccess();

            var resultStores =
                await TestApiAgainstAccessToken<StoreData[]>(accessToken, "api/test/me/stores",
                    tester.PayTester.HttpClient);
            Assert.Contains(resultStores,
                data => data.Id.Equals(testAccount.StoreId, StringComparison.InvariantCultureIgnoreCase));
            Assert.DoesNotContain(resultStores,
                data => data.Id.Equals(secondUser.StoreId, StringComparison.InvariantCultureIgnoreCase));

            Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                $"api/test/me/stores/{testAccount.StoreId}/can-edit",
                tester.PayTester.HttpClient));

            Assert.True(await TestApiAgainstAccessToken<bool>(accessToken,
                $"api/test/me/is-admin",
                tester.PayTester.HttpClient));

            await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
            {
                await TestApiAgainstAccessToken<bool>(accessToken, $"api/test/me/stores/{secondUser.StoreId}/can-edit",
                    tester.PayTester.HttpClient);
            });
        }

        public async Task<T> TestApiAgainstAccessToken<T>(string accessToken, string url, HttpClient client)
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Get,
                new Uri(client.BaseAddress, url));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
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
