using System.Threading.Tasks;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class LNbankTests : UnitTestBase
    {
        private const int TestTimeout = TestUtils.TestTimeout;

        public LNbankTests(ITestOutputHelper helper) : base(helper)
        {
        }
        
        [Fact(Timeout = TestTimeout)]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLNbank()
        {
            using var s = CreateSeleniumTester();
            s.Server.ActivateLightning();
            await s.StartAsync();
            var alice = s.RegisterNewUser(true);
            
            
            
            (string storeName, string storeId) = s.CreateNewStore();
            var storeUrl = $"/stores/{storeId}";

            s.GoToStore();
            
            // setup Lightning wallet
            s.Driver.FindElement(By.Id("SetupGuide-Lightning")).Click();
            s.AddLightningNode();
            
        }
    }
}
