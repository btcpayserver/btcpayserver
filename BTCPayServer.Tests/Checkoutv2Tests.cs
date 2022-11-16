using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Stores;
using NBitcoin;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class CheckoutV2Tests : UnitTestBase
    {
        private const int TestTimeout = TestUtils.TestTimeout;
        
        public CheckoutV2Tests(ITestOutputHelper helper) : base(helper)
        {
        }
        
        [Fact(Timeout = TestTimeout)]
        [Trait("Lightning", "Lightning")]
        public async Task CanConfigureCheckout()
        {
            using var s = CreateSeleniumTester();
            s.Server.ActivateLightning();
            await s.StartAsync();
            s.GoToRegister();
            s.RegisterNewUser(true);
            s.CreateNewStore();
            s.EnableCheckoutV2();
            s.AddLightningNode();
            s.AddDerivationScheme();

            // Default payment method
            var invoiceId = s.CreateInvoice(defaultPaymentMethod: "BTC_LightningLike");
            s.GoToInvoiceCheckout(invoiceId);
            
            Assert.Equal(2, s.Driver.FindElements(By.CssSelector(".payment-method")).Count);
            Assert.Contains("Lightning", s.Driver.FindElement(By.CssSelector(".payment-method.active")).Text);
            
            // Lightning amount in Sats
            Assert.Contains("BTC", s.Driver.FindElement(By.Id("AmountDue")).Text);
            s.GoToHome();
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LightningAmountInSatoshi"), true);
            s.Driver.FindElement(By.Id("save")).Click();
            Assert.Contains("BTC Lightning settings successfully updated", s.FindAlertMessage().Text);
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Contains("Sats", s.Driver.FindElement(By.Id("AmountDue")).Text);
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseCheckoutAsModal()
        {
            using var s = CreateSeleniumTester();
            await s.StartAsync();
            s.GoToRegister();
            s.RegisterNewUser();
            s.CreateNewStore();
            s.EnableCheckoutV2();
            s.GoToStore();
            s.AddDerivationScheme();
            var invoiceId = s.CreateInvoice(0.001m, "BTC", "a@x.com");
            var invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
            s.Driver.Navigate()
                .GoToUrl(new Uri(s.ServerUri, $"tests/index.html?invoice={invoiceId}"));
            TestUtils.Eventually(() =>
            {
                Assert.True(s.Driver.FindElement(By.Name("btcpay")).Displayed);
            });
            await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(invoice
                    .GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike))
                    .GetPaymentMethodDetails().GetPaymentDestination(), Network.RegTest),
                new Money(0.001m, MoneyUnit.BTC));

            IWebElement closebutton = null;
            TestUtils.Eventually(() =>
            {
                var frameElement = s.Driver.FindElement(By.Name("btcpay"));
                var iframe = s.Driver.SwitchTo().Frame(frameElement);
                closebutton = iframe.FindElement(By.Id("close"));
                Assert.True(closebutton.Displayed);
            });
            closebutton.Click();
            s.Driver.AssertElementNotFound(By.Name("btcpay"));
            Assert.Equal(s.Driver.Url,
                new Uri(s.ServerUri, $"tests/index.html?invoice={invoiceId}").ToString());
        }
    }
}
