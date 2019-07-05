using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Tests.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;
using Xunit.Abstractions;
using System.Net.Http;
using System.Net.Http.Headers;
using BTCPayServer.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenIddict.Abstractions;
using OpenQA.Selenium;

namespace BTCPayServer.Tests
{
    public class AuthenticationTests
    {
        public AuthenticationTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task GetRedirectedToLoginPathOnChallenge()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
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

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanGetOpenIdConfiguration()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                using (var response =
                    await tester.PayTester.HttpClient.GetAsync("/.well-known/openid-configuration"))
                {
                    using (var streamToReadFrom = new StreamReader(await response.Content.ReadAsStreamAsync()))
                    {
                        var json = await streamToReadFrom.ReadToEndAsync();
                        Assert.NotNull(json);
                        var configuration = OpenIdConnectConfiguration.Create(json);
                        Assert.NotNull(configuration);
                    }
                }
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUseNonInteractiveFlows()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();

                var user = tester.NewAccount();
                user.GrantAccess();
                var token = await RegisterPasswordClientAndGetAccessToken(user, null, tester);
                await TestApiAgainstAccessToken(token, tester, user);
                token = await RegisterPasswordClientAndGetAccessToken(user, "secret", tester);
                await TestApiAgainstAccessToken(token, tester, user);
                token = await RegisterClientCredentialsFlowAndGetAccessToken(user, "secret", tester);
                await TestApiAgainstAccessToken(token, tester, user);
            }
        }

        [Trait("Selenium", "Selenium")]
        [Fact]
        public async Task CanUseImplicitFlow()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                var tester = s.Server;

                var user = tester.NewAccount();
                user.GrantAccess();
                var id = Guid.NewGuid().ToString();
                var redirecturi = new Uri("http://127.0.0.1/oidc-callback");
                var openIdClient = await user.RegisterOpenIdClient(
                    new OpenIddictApplicationDescriptor()
                    {
                        ClientId = id,
                        DisplayName = id,
                        Permissions = {OpenIddictConstants.Permissions.GrantTypes.Implicit},
                        RedirectUris = {redirecturi}
                    });
                var implicitAuthorizeUrl = new Uri(tester.PayTester.ServerUri,
                    $"connect/authorize?response_type=token&client_id={id}&redirect_uri={redirecturi.AbsoluteUri}&scope=openid&nonce={Guid.NewGuid().ToString()}");
                s.Driver.Navigate().GoToUrl(implicitAuthorizeUrl);
                s.Login(user.RegisterDetails.Email, user.RegisterDetails.Password);
                var url = s.Driver.Url;
                var results = url.Split("#").Last().Split("&")
                    .ToDictionary(s1 => s1.Split("=")[0], s1 => s1.Split("=")[1]);
                await TestApiAgainstAccessToken(results["access_token"], tester, user);


                //in Implicit mode, you renew your token  by hitting the same endpoint but adding prompt=none. If you are still logged in on the site, you will receive a fresh token.
                var implicitAuthorizeUrlSilentModel = new Uri($"{implicitAuthorizeUrl.OriginalString}&prompt=none");
                s.Driver.Navigate().GoToUrl(implicitAuthorizeUrl);
                url = s.Driver.Url;
                results = url.Split("#").Last().Split("&").ToDictionary(s1 => s1.Split("=")[0], s1 => s1.Split("=")[1]);
                await TestApiAgainstAccessToken(results["access_token"], tester, user);

                LogoutFlow(tester, id, s);
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

        [Trait("Selenium", "Selenium")]
        [Fact]
        public async Task CanUseCodeFlow()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                var tester = s.Server;

                var user = tester.NewAccount();
                user.GrantAccess();
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
                    $"connect/authorize?response_type=code&client_id={id}&redirect_uri={redirecturi.AbsoluteUri}&scope=openid offline_access&state={Guid.NewGuid().ToString()}");
                s.Driver.Navigate().GoToUrl(authorizeUrl);
                s.Login(user.RegisterDetails.Email, user.RegisterDetails.Password);
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
                var result = JObject.Parse(content).ToObject<OpenIdConnectResponse>();

                await TestApiAgainstAccessToken(result.AccessToken, tester, user);

                var refreshedAccessToken = await RefreshAnAccessToken(result.RefreshToken, httpClient, id, secret);

                await TestApiAgainstAccessToken(refreshedAccessToken, tester, user);
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
            var result = JObject.Parse(content).ToObject<OpenIdConnectResponse>();
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
                    new KeyValuePair<string, string>("client_secret", secret)
                })
            };


            var response = await httpClient.SendAsync(httpRequest);

            Assert.True(response.IsSuccessStatusCode);

            string content = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(content).ToObject<OpenIdConnectResponse>();
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
                    new KeyValuePair<string, string>("client_secret", secret)
                })
            };


            var response = await httpClient.SendAsync(httpRequest);

            Assert.True(response.IsSuccessStatusCode);

            string content = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(content).ToObject<OpenIdConnectResponse>();
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


            Assert.Equal(testAccount.RegisterDetails.IsAdmin, await TestApiAgainstAccessToken<bool>(accessToken,
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
