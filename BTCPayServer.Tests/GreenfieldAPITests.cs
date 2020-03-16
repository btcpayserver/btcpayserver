using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNetCore.Mvc;
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

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task ApiKeysControllerTests()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                var client = await user.CreateClient(Permissions.ServerManagement, Permissions.StoreManagement);
                //Get current api key 
                var apiKeyData = await client.GetCurrentAPIKeyInfo();
                Assert.NotNull(apiKeyData);
                Assert.Equal(client.APIKey, apiKeyData.ApiKey);
                Assert.Equal(user.UserId, apiKeyData.UserId);
                Assert.Equal(2, apiKeyData.Permissions.Length);
                
                //revoke current api key
                await client.RevokeCurrentAPIKeyInfo();
                await Assert.ThrowsAsync<HttpRequestException>(async () =>
                {
                    await client.GetCurrentAPIKeyInfo();
                });
            }
        }
        
        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task UsersControllerTests()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                await user.MakeAdmin();
                var clientProfile = await user.CreateClient(Permissions.ProfileManagement);
                var clientServer = await user.CreateClient(Permissions.ServerManagement);
                var clientInsufficient = await user.CreateClient(Permissions.StoreManagement);


                var apiKeyProfileUserData = await clientProfile.GetCurrentUser();
                Assert.NotNull(apiKeyProfileUserData);
                Assert.Equal(apiKeyProfileUserData.Id, user.UserId);
                Assert.Equal(apiKeyProfileUserData.Email, user.RegisterDetails.Email);

                await Assert.ThrowsAsync<HttpRequestException>(async () => await clientInsufficient.GetCurrentUser());
                await clientServer.GetCurrentUser();
            }
        }
    }
}
