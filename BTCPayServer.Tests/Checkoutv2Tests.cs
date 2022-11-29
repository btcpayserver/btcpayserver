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

            // Configure store url
            var storeUrl = "https://satoshisteaks.com/";
            s.GoToStore();
            s.Driver.FindElement(By.Id("StoreWebsite")).SendKeys(storeUrl);
            s.Driver.FindElement(By.Id("Save")).Click();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);
            
            // Default payment method
            var invoiceId = s.CreateInvoice(defaultPaymentMethod: "BTC_LightningLike");
            s.GoToInvoiceCheckout(invoiceId);
            
            Assert.Equal(2, s.Driver.FindElements(By.CssSelector(".payment-method")).Count);
            Assert.Contains("Lightning", s.Driver.WaitForElement(By.CssSelector(".payment-method.active")).Text);
            Assert.DoesNotContain("LNURL", s.Driver.PageSource);
            var payUrl = s.Driver.FindElement(By.CssSelector(".btn-primary")).GetAttribute("href");
            Assert.StartsWith("lightning:", payUrl);
            
            // Lightning amount in Sats
            Assert.Contains("BTC", s.Driver.FindElement(By.Id("AmountDue")).Text);
            s.GoToHome();
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LightningAmountInSatoshi"), true);
            s.Driver.FindElement(By.Id("save")).Click();
            Assert.Contains("BTC Lightning settings successfully updated", s.FindAlertMessage().Text);
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Contains("Sats", s.Driver.FindElement(By.Id("AmountDue")).Text);
            
            // Expire
            var expirySeconds = s.Driver.FindElement(By.Id("ExpirySeconds"));
            expirySeconds.Clear();
            expirySeconds.SendKeys("3");
            s.Driver.FindElement(By.Id("Expire")).Click();

            var paymentInfo = s.Driver.WaitForElement(By.Id("PaymentInfo"));
            Assert.Contains("This invoice will expire in", paymentInfo.Text);
            Assert.DoesNotContain("Please send", paymentInfo.Text);
            TestUtils.Eventually(() =>
            {
                var expiredSection = s.Driver.FindElement(By.Id("expired"));
                Assert.True(expiredSection.Displayed);
                Assert.Contains("Invoice Expired", expiredSection.Text);
            });
            Assert.True(s.Driver.ElementDoesNotExist(By.Id("ReceiptLink")));
            Assert.Equal(storeUrl, s.Driver.FindElement(By.Id("StoreLink")).GetAttribute("href"));
            
            // Test payment
            s.GoToHome();
            invoiceId = s.CreateInvoice();
            s.GoToInvoiceCheckout(invoiceId);
            
            // Details
            s.Driver.ToggleCollapse("PaymentDetails");
            var details = s.Driver.FindElement(By.CssSelector(".payment-details"));
            Assert.Contains("Total Price", details.Text);
            Assert.Contains("Total Fiat", details.Text);
            Assert.Contains("Exchange Rate", details.Text);
            Assert.Contains("Amount Due", details.Text);
            Assert.Contains("Recommended Fee", details.Text);
            
            // Pay partial amount
            await Task.Delay(200);
            var address = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-destination");
            var amountFraction = "0.00001";
            await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(address, Network.RegTest),
                Money.Parse(amountFraction));
            await s.Server.ExplorerNode.GenerateAsync(1);
            
            // Fake Pay
            s.Driver.FindElement(By.Id("FakePayAmount")).FillIn(amountFraction);
            s.Driver.FindElement(By.Id("FakePay")).Click();
            TestUtils.Eventually(() =>
            {
                Assert.Contains("Created transaction",
                    s.Driver.WaitForElement(By.Id("CheatSuccessMessage")).Text);
                s.Server.ExplorerNode.Generate(1);
                paymentInfo = s.Driver.WaitForElement(By.Id("PaymentInfo"));
                Assert.Contains("The invoice hasn't been paid in full", paymentInfo.Text);
                Assert.Contains("Please send", paymentInfo.Text);
            });

            // Mine
            s.Driver.FindElement(By.Id("Mine")).Click();
            TestUtils.Eventually(() =>
            {
                Assert.Contains("Mined 1 block",
                    s.Driver.WaitForElement(By.Id("CheatSuccessMessage")).Text);
            });
            
            // Pay full amount
            var amountDue = s.Driver.FindElement(By.Id("AmountDue")).GetAttribute("data-amount-due");
            s.Driver.FindElement(By.Id("FakePayAmount")).FillIn(amountDue);
            s.Driver.FindElement(By.Id("FakePay")).Click();
            TestUtils.Eventually(() =>
            {
                s.Server.ExplorerNode.Generate(1);
                var paidSection = s.Driver.WaitForElement(By.Id("paid"));
                Assert.True(paidSection.Displayed);
                Assert.Contains("Invoice Paid", paidSection.Text);
            });
            s.Driver.FindElement(By.Id("ReceiptLink"));
            Assert.Equal(storeUrl, s.Driver.FindElement(By.Id("StoreLink")).GetAttribute("href"));
            
            // BIP21
            s.GoToHome();
            s.GoToStore(StoreNavPages.CheckoutAppearance);
            s.Driver.SetCheckbox(By.Id("OnChainWithLnInvoiceFallback"), true);
            s.Driver.FindElement(By.Id("Save")).Click();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);
            
            invoiceId = s.CreateInvoice();
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            payUrl = s.Driver.FindElement(By.CssSelector(".btn-primary")).GetAttribute("href");
            Assert.StartsWith("bitcoin:", payUrl);
            Assert.Contains("&LIGHTNING=", payUrl);
            
            // BIP21 with LN as default payment method
            s.GoToHome();
            invoiceId = s.CreateInvoice(defaultPaymentMethod: "BTC_LightningLike");
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            payUrl = s.Driver.FindElement(By.CssSelector(".btn-primary")).GetAttribute("href");
            Assert.StartsWith("bitcoin:", payUrl);
            Assert.Contains("&LIGHTNING=", payUrl);
            
            // BIP21 with topup invoice (which is only available with Bitcoin onchain)
            s.GoToHome();
            invoiceId = s.CreateInvoice(amount: null);
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            payUrl = s.Driver.FindElement(By.CssSelector(".btn-primary")).GetAttribute("href");
            Assert.StartsWith("bitcoin:", payUrl);
            Assert.DoesNotContain("&LIGHTNING=", payUrl);
            
            // Expiry message should not show amount for topup invoice
            expirySeconds = s.Driver.FindElement(By.Id("ExpirySeconds"));
            expirySeconds.Clear();
            expirySeconds.SendKeys("5");
            s.Driver.FindElement(By.Id("Expire")).Click();

            paymentInfo = s.Driver.WaitForElement(By.Id("PaymentInfo"));
            Assert.Contains("This invoice will expire in", paymentInfo.Text);
            Assert.DoesNotContain("Please send", paymentInfo.Text);
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
