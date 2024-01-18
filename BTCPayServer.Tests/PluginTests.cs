using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class PluginTests : UnitTestBase
    {
        private const int TestTimeout = TestUtils.TestTimeout;

        public PluginTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanManagePlugins()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.GoToRegister();
            s.RegisterNewUser(true);

            s.GoToUrl("/server/plugins");
            Assert.Contains("Available Plugins", s.Driver.PageSource);
            Assert.DoesNotContain("Disabled Plugins", s.Driver.PageSource);
            Assert.DoesNotContain("Installed Plugins", s.Driver.PageSource);

            // Upload broken version
            UploadPlugin(s, "Plugins/LNbank-v1.6.0/BTCPayServer.Plugins.LNbank.btcpay");
            
            // Restart and check for error
            await Assert.ThrowsAsync<System.TypeLoadException>(async () =>
            {
                await RestartAfterInstall(s);
            });
            // Restart once more with plugin disabled
            await s.Server.PayTester.StartAsync();

            // Broken plugin should be disabled
            s.GoToUrl("/server/plugins");
            Assert.Contains("Some plugins were disabled due to fatal errors. They may be incompatible with this version of BTCPay Server.", s.FindAlertMessage(StatusMessageModel.StatusSeverity.Error).Text);
            Assert.Contains("Disabled Plugins", s.Driver.PageSource);
            Assert.DoesNotContain("Installed Plugins", s.Driver.PageSource);

            var disabledPlugins = s.Driver.FindElements(By.CssSelector("#DisabledPlugins li"));
            Assert.Single(disabledPlugins);
            disabledPlugins.First().FindElement(By.CssSelector(".uninstall-plugin")).Click();
            Assert.Contains("Plugin scheduled to be uninstalled", s.FindAlertMessage().Text);

            // Restart
            await RestartAfterInstall(s);
            
            s.GoToUrl("/server/plugins");
            Assert.Contains("Available Plugins", s.Driver.PageSource);
            Assert.DoesNotContain("Disabled Plugins", s.Driver.PageSource);
            Assert.DoesNotContain("Installed Plugins", s.Driver.PageSource);

            // Install working plugin
            UploadPlugin(s, "Plugins/LNbank-v1.9.2/BTCPayServer.Plugins.LNbank.btcpay");
        }

        private static void UploadPlugin(SeleniumTester s, string pluginPath)
        {
            s.Driver.ToggleCollapse("manual-upload");
            s.Driver.FindElement(By.Id("files")).SendKeys(TestUtils.GetTestDataFullPath(pluginPath));
            s.Driver.FindElement(By.Id("UploadPlugin")).Click();
            Assert.Contains("Files uploaded, restart server to load plugins", s.FindAlertMessage().Text);
        }

        private static async Task RestartAfterInstall(SeleniumTester s)
        {
            Assert.Contains("You need to restart BTCPay Server in order to update your active plugins", s.FindAlertMessage(StatusMessageModel.StatusSeverity.Info).Text);
            s.Driver.FindElement(By.Id("Restart")).Click();
            Assert.Contains("BTCPay will restart momentarily", s.FindAlertMessage().Text);
            s.Server.PayTester.Dispose();
            await s.Server.PayTester.StartAsync();
        }
    }
}
