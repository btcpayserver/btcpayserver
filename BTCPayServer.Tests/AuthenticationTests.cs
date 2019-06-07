using System;
using System.Collections.Generic;
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
        public void CanGetOpenIdConfiguration()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var url = new Uri(tester.PayTester.ServerUri, "/.well-known/openid-configuration");
                using (var wc = new WebClient())
                {
                    var json = wc.DownloadString(url);
                    Assert.NotNull(json);
                    var configuration = OpenIdConnectConfiguration.Create(json);
                    Assert.NotNull(configuration);
                }
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanUseNonInteractiveFlows()
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

        public async Task TestApiAgainstAccessToken(string accessToken, ServerTester tester, TestAccount testAccount)
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

            Assert.True(await TestApiAgainstAccessToken<bool>(accessToken, $"api/test/me/stores/{testAccount.StoreId}/can-edit",
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
