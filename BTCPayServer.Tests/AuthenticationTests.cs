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
using Newtonsoft.Json.Linq;

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
        public async void CanGetAccessToken()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();

                var user = tester.NewAccount();
                user.GrantAccess();

                await GetAccessTokenByPasswordGrant(tester, user);
            }
        }

        private static async Task<OpenIdConnectResponse> GetAccessTokenByPasswordGrant(ServerTester tester,
            TestAccount user)
        {
            using (var httpClient = new HttpClient())
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post,
                    new Uri(tester.PayTester.ServerUri, "/connect/token"))
                {
                    Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
                    {
                        new KeyValuePair<string, string>("grant_type", "password"),
                        new KeyValuePair<string, string>("username", user.RegisterViewModel.Email),
                        new KeyValuePair<string, string>("password", user.RegisterViewModel.Password)
                    })
                };


                var response = await httpClient.SendAsync(httpRequest);

                Assert.True(response.IsSuccessStatusCode);

                string content = await response.Content.ReadAsStringAsync();
                var result = JObject.Parse(content).ToObject<OpenIdConnectResponse>();
                Assert.NotEmpty(result.AccessToken);
                Assert.Null(result.Error);

                return result;
            }
        }
        [Fact]
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
    }
}
