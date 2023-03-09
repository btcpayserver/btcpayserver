using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Stores;
using NBitcoin;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
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
            // Use non-legacy derivation scheme
            s.AddDerivationScheme("BTC", "tpubDD79XF4pzhmPSJ9AyUay9YbXAeD1c6nkUqC32pnKARJH6Ja5hGUfGc76V82ahXpsKqN6UcSGXMkzR34aZq4W23C6DAdZFaVrzWqzj24F8BC");

            // Configure store url
            var storeUrl = "https://satoshisteaks.com/";
            s.GoToStore();
            s.Driver.FindElement(By.Id("StoreWebsite")).SendKeys(storeUrl);
            s.Driver.FindElement(By.Id("Save")).Click();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);
            
            // Enable LNURL, which we will need for (non-)presence checks throughout this test
            s.GoToHome();
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LNURLEnabled"), true);
            s.Driver.SetCheckbox(By.Id("LNURLStandardInvoiceEnabled"), true);
            s.Driver.FindElement(By.Id("save")).Click();
            Assert.Contains("BTC Lightning settings successfully updated", s.FindAlertMessage().Text);
            
            // Top up/zero amount invoices
            var invoiceId = s.CreateInvoice(amount: null);
            s.GoToInvoiceCheckout(invoiceId);

            // Ensure we are seeing Checkout v2
            s.Driver.WaitUntilAvailable(By.Id("Checkout-v2"));
            Assert.Equal(2, s.Driver.FindElements(By.CssSelector(".payment-method")).Count);
            Assert.Contains("Bitcoin", s.Driver.FindElement(By.CssSelector(".payment-method.active")).Text);
            Assert.Contains("LNURL", s.Driver.FindElement(By.CssSelector(".payment-method:nth-child(2)")).Text);
            var qrValue = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-qr-value");
            var address = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-clipboard");
            var payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            var copyAddress = s.Driver.FindElement(By.Id("Address_BTC")).GetAttribute("value");
            Assert.Equal($"bitcoin:{address}", payUrl);
            Assert.StartsWith("bcrt", s.Driver.FindElement(By.Id("Address_BTC")).GetAttribute("value"));
            Assert.DoesNotContain("lightning=", payUrl);
            Assert.Equal(address, copyAddress);
            Assert.Equal($"bitcoin:{address.ToUpperInvariant()}", qrValue);
            s.Driver.ElementDoesNotExist(By.Id("Lightning_BTC"));
            s.Driver.ElementDoesNotExist(By.Id("PayByLNURL"));
            
            // Switch to LNURL
            s.Driver.FindElement(By.CssSelector(".payment-method:nth-child(2)")).Click();
            TestUtils.Eventually(() =>
            {
                payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
                Assert.StartsWith("lightning:lnurl", payUrl);
                Assert.StartsWith("lnurl", s.Driver.WaitForElement(By.Id("Lightning_BTC")).GetAttribute("value"));
                s.Driver.ElementDoesNotExist(By.Id("Address_BTC"));
                s.Driver.FindElement(By.Id("PayByLNURL"));
            });

            // Default payment method
            s.GoToHome();
            invoiceId = s.CreateInvoice(defaultPaymentMethod: "BTC_LightningLike");
            s.GoToInvoiceCheckout(invoiceId);
            s.Driver.WaitUntilAvailable(By.Id("Checkout-v2"));
            Assert.Equal(2, s.Driver.FindElements(By.CssSelector(".payment-method")).Count);
            Assert.Contains("Lightning", s.Driver.WaitForElement(By.CssSelector(".payment-method.active")).Text);
            Assert.Contains("Bitcoin", s.Driver.WaitForElement(By.CssSelector(".payment-method")).Text);
            qrValue = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-qr-value");
            address = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-clipboard");
            payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            copyAddress = s.Driver.FindElement(By.Id("Lightning_BTC_LightningLike")).GetAttribute("value");
            Assert.Equal($"lightning:{address}", payUrl);
            Assert.Equal(address, copyAddress);
            Assert.Equal($"lightning:{address.ToUpperInvariant()}", qrValue);
            s.Driver.ElementDoesNotExist(By.Id("Address_BTC"));
            s.Driver.FindElement(By.Id("PayByLNURL"));

            // Lightning amount in sats
            Assert.Contains("BTC", s.Driver.FindElement(By.Id("AmountDue")).Text);
            s.GoToHome();
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LightningAmountInSatoshi"), true);
            s.Driver.FindElement(By.Id("save")).Click();
            Assert.Contains("BTC Lightning settings successfully updated", s.FindAlertMessage().Text);
            s.GoToInvoiceCheckout(invoiceId);
            s.Driver.WaitUntilAvailable(By.Id("Checkout-v2"));
            Assert.Contains("sats", s.Driver.FindElement(By.Id("AmountDue")).Text);

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
            s.Driver.WaitUntilAvailable(By.Id("Checkout-v2"));

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
            address = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-clipboard");
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
            s.Driver.SetCheckbox(By.Id("LightningAmountInSatoshi"), false);
            s.Driver.FindElement(By.Id("Save")).Click();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);

            invoiceId = s.CreateInvoice();
            s.GoToInvoiceCheckout(invoiceId);
            s.Driver.WaitUntilAvailable(By.Id("Checkout-v2"));
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            Assert.Contains("BTC", s.Driver.FindElement(By.Id("AmountDue")).Text);
            qrValue = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-qr-value");
            address = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-clipboard");
            payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            var copyAddressOnchain = s.Driver.FindElement(By.Id("Address_BTC")).GetAttribute("value");
            var copyAddressLightning = s.Driver.FindElement(By.Id("Lightning_BTC")).GetAttribute("value");
            Assert.StartsWith($"bitcoin:{address}?amount=", payUrl);
            Assert.Contains("?amount=", payUrl);
            Assert.Contains("&lightning=", payUrl);
            Assert.StartsWith("bcrt", copyAddressOnchain);
            Assert.Equal(address, copyAddressOnchain);
            Assert.StartsWith("lnbcrt", copyAddressLightning);
            Assert.StartsWith($"bitcoin:{address.ToUpperInvariant()}?amount=", qrValue);
            Assert.Contains("&lightning=LNBCRT", qrValue);
            s.Driver.FindElement(By.Id("PayByLNURL"));
            
            // Switch to amount displayed in sats
            s.GoToHome();
            s.GoToStore(StoreNavPages.CheckoutAppearance);
            s.Driver.SetCheckbox(By.Id("LightningAmountInSatoshi"), true);
            s.Driver.FindElement(By.Id("Save")).Click();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);
            s.GoToInvoiceCheckout(invoiceId);
            s.Driver.WaitUntilAvailable(By.Id("Checkout-v2"));
            Assert.Contains("sats", s.Driver.FindElement(By.Id("AmountDue")).Text);

            // BIP21 with LN as default payment method
            s.GoToHome();
            invoiceId = s.CreateInvoice(defaultPaymentMethod: "BTC_LightningLike");
            s.GoToInvoiceCheckout(invoiceId);
            s.Driver.WaitUntilAvailable(By.Id("Checkout-v2"));
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            Assert.StartsWith("bitcoin:", payUrl);
            Assert.Contains("&lightning=lnbcrt", payUrl);
            s.Driver.FindElement(By.Id("PayByLNURL"));

            // Ensure LNURL is enabled
            s.GoToHome();
            s.GoToLightningSettings();
            Assert.True(s.Driver.FindElement(By.Id("LNURLEnabled")).Selected);
            Assert.True(s.Driver.FindElement(By.Id("LNURLStandardInvoiceEnabled")).Selected);
            
            // BIP21 with top-up invoice
            invoiceId = s.CreateInvoice(amount: null);
            s.GoToInvoiceCheckout(invoiceId);
            s.Driver.WaitUntilAvailable(By.Id("Checkout-v2"));
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            qrValue = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-qr-value");
            address = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-clipboard");
            payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            copyAddressOnchain = s.Driver.FindElement(By.Id("Address_BTC")).GetAttribute("value");
            copyAddressLightning = s.Driver.FindElement(By.Id("Lightning_BTC")).GetAttribute("value");
            Assert.StartsWith($"bitcoin:{address}", payUrl);
            Assert.Contains("?lightning=lnurl", payUrl);
            Assert.DoesNotContain("amount=", payUrl);
            Assert.StartsWith("bcrt", copyAddressOnchain);
            Assert.Equal(address, copyAddressOnchain);
            Assert.StartsWith("lnurl", copyAddressLightning);
            Assert.StartsWith($"bitcoin:{address.ToUpperInvariant()}?lightning=LNURL", qrValue);
            s.Driver.FindElement(By.Id("PayByLNURL"));

            // Expiry message should not show amount for top-up invoice
            expirySeconds = s.Driver.FindElement(By.Id("ExpirySeconds"));
            expirySeconds.Clear();
            expirySeconds.SendKeys("5");
            s.Driver.FindElement(By.Id("Expire")).Click();

            paymentInfo = s.Driver.WaitForElement(By.Id("PaymentInfo"));
            Assert.Contains("This invoice will expire in", paymentInfo.Text);
            Assert.Contains("00:0", paymentInfo.Text);
            Assert.DoesNotContain("Please send", paymentInfo.Text);
            
            // Configure countdown timer
            s.GoToHome();
            invoiceId = s.CreateInvoice();
            s.GoToHome();
            s.GoToStore(StoreNavPages.CheckoutAppearance);
            var displayExpirationTimer = s.Driver.FindElement(By.Id("DisplayExpirationTimer"));
            Assert.Equal("5", displayExpirationTimer.GetAttribute("value"));
            displayExpirationTimer.Clear();
            displayExpirationTimer.SendKeys("10");
            s.Driver.FindElement(By.Id("Save")).Click();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);
            
            s.GoToInvoiceCheckout(invoiceId);
            s.Driver.WaitUntilAvailable(By.Id("Checkout-v2"));
            paymentInfo = s.Driver.FindElement(By.Id("PaymentInfo"));
            Assert.False(paymentInfo.Displayed);
            Assert.DoesNotContain("This invoice will expire in", paymentInfo.Text);
            
            expirySeconds = s.Driver.FindElement(By.Id("ExpirySeconds"));
            expirySeconds.Clear();
            expirySeconds.SendKeys("599");
            s.Driver.FindElement(By.Id("Expire")).Click();

            paymentInfo = s.Driver.WaitForElement(By.Id("PaymentInfo"));
            Assert.True(paymentInfo.Displayed);
            Assert.Contains("This invoice will expire in", paymentInfo.Text);
            Assert.Contains("09:5", paymentInfo.Text);
            
            // Disable LNURL again
            s.GoToHome();
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LNURLEnabled"), false);
            s.Driver.FindElement(By.Id("save")).Click();
            Assert.Contains("BTC Lightning settings successfully updated", s.FindAlertMessage().Text);

            // Test:
            // - NFC/LNURL-W available with just Lightning
            // - BIP21 works correctly even though Lightning is default payment method
            s.GoToHome();
            invoiceId = s.CreateInvoice(defaultPaymentMethod: "BTC_LightningLike");
            s.GoToInvoiceCheckout(invoiceId);
            s.Driver.WaitUntilAvailable(By.Id("Checkout-v2"));
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            Assert.StartsWith("bitcoin:", payUrl);
            Assert.Contains("&lightning=lnbcrt", payUrl);
            s.Driver.FindElement(By.Id("PayByLNURL"));
            
            // Language Switch
            var languageSelect = new SelectElement(s.Driver.FindElement(By.Id("DefaultLang")));
            Assert.Equal("English", languageSelect.SelectedOption.Text);
            Assert.Equal("View Details", s.Driver.FindElement(By.Id("DetailsToggle")).Text);
            Assert.DoesNotContain("lang=", s.Driver.Url);
            languageSelect.SelectByText("Deutsch");
            Assert.Equal("Details anzeigen", s.Driver.FindElement(By.Id("DetailsToggle")).Text);
            Assert.Contains("lang=de", s.Driver.Url);
            
            s.Driver.Navigate().Refresh();
            languageSelect = new SelectElement(s.Driver.FindElement(By.Id("DefaultLang")));
            Assert.Equal("Deutsch", languageSelect.SelectedOption.Text);
            Assert.Equal("Details anzeigen", s.Driver.FindElement(By.Id("DetailsToggle")).Text);
            languageSelect.SelectByText("English");
            Assert.Equal("View Details", s.Driver.FindElement(By.Id("DetailsToggle")).Text);
            Assert.Contains("lang=en", s.Driver.Url);
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
            s.Driver.WaitUntilAvailable(By.Name("btcpay"));

            var frameElement = s.Driver.FindElement(By.Name("btcpay"));
            Assert.True(frameElement.Displayed);
            var iframe = s.Driver.SwitchTo().Frame(frameElement);
            iframe.WaitUntilAvailable(By.Id("Checkout-v2"));

            await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(invoice
                    .GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike))
                    .GetPaymentMethodDetails().GetPaymentDestination(), Network.RegTest),
                new Money(0.001m, MoneyUnit.BTC));

            IWebElement closebutton = null;
            TestUtils.Eventually(() =>
            {
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
