using System;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Primitives;
using BTCPayServer.Controllers;
using ExchangeSharp;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using Xunit;

namespace BTCPayServer.Tests
{
    public class AuthenticationTests
    {
        [Fact]
        public async void CanGenerateAccessTokenForUserWithPasswordGrant()
        {
            using (var tester = ServerTester.Create())
            {
                OpenIdConnectResponse response = await GetAccessTokenWithPasswordGrant(tester, false);
                Assert.Null(response.Error);
                Assert.NotNull(response.AccessToken);
                Assert.Equal("Bearer", response.TokenType);
            }
        }

        private static async Task<OpenIdConnectResponse> GetAccessTokenWithPasswordGrant(ServerTester tester,
            bool invalidCredentials)
        {
            tester.Start();
            var user = tester.NewAccount();
            user.GrantAccess();
            user.RegisterDerivationScheme("BTC");

            var authorizationController = user.GetController<AuthorizationController>();

            var exchangeResult = await authorizationController.Exchange(new OpenIdConnectRequest()
            {
                GrantType = "password",
                Username = user.RegisterViewModel.Email,
                Password = invalidCredentials ? Guid.NewGuid().ToString() : user.RegisterViewModel.Password
            });

            var response = Assert.IsType<OpenIdConnectResponse>(Assert.IsType<JsonResult>(exchangeResult).Value);
            return response;
        }

        [Fact]
        public async void CanGenerateAccessTokenForUserWithPasswordGrant_InvalidCredentials()
        {
            using (var tester = ServerTester.Create())
            {
                OpenIdConnectResponse response = await GetAccessTokenWithPasswordGrant(tester, true);
                Assert.NotNull(response.Error);
                Assert.Equal(OpenIdConnectConstants.Errors.InvalidGrant, response.Error);
                Assert.Null(response.AccessToken);
            }
        }
    }
    
    
}
