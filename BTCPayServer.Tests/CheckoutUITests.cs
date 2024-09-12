using System;
using System.Threading.Tasks;
using BTCPayServer.Payments;
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
    public class CheckoutUITests : UnitTestBase
    {
        public const int TestTimeout = TestUtils.TestTimeout;
        public CheckoutUITests(ITestOutputHelper helper) : base(helper)
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
            s.AddLightningNode();
            // Use non-legacy derivation scheme
            s.AddDerivationScheme("BTC", "tpubDD79XF4pzhmPSJ9AyUay9YbXAeD1c6nkUqC32pnKARJH6Ja5hGUfGc76V82ahXpsKqN6UcSGXMkzR34aZq4W23C6DAdZFaVrzWqzj24F8BC");

            // Configure store url
            var storeUrl = "https://satoshisteaks.com/";
            var supportUrl = "https://support.satoshisteaks.com/{InvoiceId}/";
            s.GoToStore();
            s.Driver.FindElement(By.Id("StoreWebsite")).SendKeys(storeUrl);
            s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);

            s.GoToStore(StoreNavPages.CheckoutAppearance);
            s.Driver.WaitForAndClick(By.Id("Presets"));
            s.Driver.WaitForAndClick(By.Id("Presets_InStore"));
            Assert.True(s.Driver.SetCheckbox(By.Id("ShowPayInWalletButton"), true));
            s.Driver.FindElement(By.Id("SupportUrl")).SendKeys(supportUrl);
            s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);

            // Top up/zero amount invoices
            var invoiceId = s.CreateInvoice(amount: null);
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Equal(2, s.Driver.FindElements(By.CssSelector(".payment-method")).Count);
            Assert.Contains("Bitcoin", s.Driver.FindElement(By.CssSelector(".payment-method.active")).Text);
            Assert.Contains("LNURL", s.Driver.FindElement(By.CssSelector(".payment-method:nth-child(2)")).Text);
            var qrValue = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-qr-value");
            var clipboard = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-clipboard");
            var payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            var address = s.Driver.FindElement(By.CssSelector("#Address_BTC-CHAIN .truncate-center")).GetAttribute("data-text");
            Assert.StartsWith("bcrt", address);
            Assert.DoesNotContain("lightning=", payUrl);
            Assert.Equal($"bitcoin:{address}", payUrl);
            Assert.Equal($"bitcoin:{address}", clipboard);
            Assert.Equal($"bitcoin:{address.ToUpperInvariant()}", qrValue);
            s.Driver.ElementDoesNotExist(By.Id("Lightning_BTC-CHAIN"));

            // Details should show exchange rate
            s.Driver.ToggleCollapse("PaymentDetails");
            s.Driver.ElementDoesNotExist(By.Id("PaymentDetails-TotalPrice"));
            s.Driver.ElementDoesNotExist(By.Id("PaymentDetails-TotalFiat"));
            s.Driver.ElementDoesNotExist(By.Id("PaymentDetails-AmountDue"));
            Assert.Contains("$", s.Driver.FindElement(By.Id("PaymentDetails-ExchangeRate")).Text);
            Assert.Contains("sat/byte", s.Driver.FindElement(By.Id("PaymentDetails-RecommendedFee")).Text);

            // Switch to LNURL
            s.Driver.FindElement(By.CssSelector(".payment-method:nth-child(2)")).Click();
            TestUtils.Eventually(() =>
            {
                payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
                Assert.StartsWith("lightning:lnurl", payUrl);
                Assert.StartsWith("lnurl", s.Driver.WaitForElement(By.CssSelector("#Lightning_BTC-CHAIN .truncate-center")).GetAttribute("data-text"));
                s.Driver.ElementDoesNotExist(By.Id("Address_BTC-CHAIN"));
            });

            // Default payment method
            s.GoToHome();
            invoiceId = s.CreateInvoice(21000, "SATS", defaultPaymentMethod: "BTC-LN");
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Equal(2, s.Driver.FindElements(By.CssSelector(".payment-method")).Count);
            Assert.Contains("Lightning", s.Driver.WaitForElement(By.CssSelector(".payment-method.active")).Text);
            Assert.Contains("Bitcoin", s.Driver.WaitForElement(By.CssSelector(".payment-method")).Text);
            qrValue = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-qr-value");
            clipboard = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-clipboard");
            payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            address = s.Driver.FindElement(By.CssSelector("#Lightning_BTC-LN .truncate-center")).GetAttribute("data-text");
            Assert.Equal($"lightning:{address}", payUrl);
            Assert.Equal($"lightning:{address}", clipboard);
            Assert.Equal($"lightning:{address.ToUpperInvariant()}", qrValue);
            s.Driver.ElementDoesNotExist(By.Id("Address_BTC-CHAIN"));

            // Lightning amount in sats
            Assert.Contains("BTC", s.Driver.FindElement(By.Id("AmountDue")).Text);
            s.GoToHome();
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LightningAmountInSatoshi"), true);
            s.ClickPagePrimary();
            Assert.Contains("BTC Lightning settings successfully updated", s.FindAlertMessage().Text);
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Contains("sats", s.Driver.FindElement(By.Id("AmountDue")).Text);

            // Details should not show exchange rate
            s.Driver.ToggleCollapse("PaymentDetails");
            s.Driver.ElementDoesNotExist(By.Id("PaymentDetails-ExchangeRate"));
            s.Driver.ElementDoesNotExist(By.Id("PaymentDetails-TotalFiat"));
            s.Driver.ElementDoesNotExist(By.Id("PaymentDetails-RecommendedFee"));
            Assert.Contains("21 000 sats", s.Driver.FindElement(By.Id("PaymentDetails-AmountDue")).Text);
            Assert.Contains("21 000 sats", s.Driver.FindElement(By.Id("PaymentDetails-TotalPrice")).Text);

            // Expire
            var expirySeconds = s.Driver.FindElement(By.Id("ExpirySeconds"));
            expirySeconds.Clear();
            expirySeconds.SendKeys("3");
            s.Driver.FindElement(By.Id("Expire")).Click();

            TestUtils.Eventually(() =>
            {
                var paymentInfo = s.Driver.WaitForElement(By.Id("PaymentInfo"));
                Assert.Contains("This invoice will expire in", paymentInfo.Text);
                Assert.DoesNotContain("Please send", paymentInfo.Text);
            });
            TestUtils.Eventually(() =>
            {
                var expiredSection = s.Driver.FindElement(By.Id("unpaid"));
                Assert.True(expiredSection.Displayed);
                Assert.Contains("Invoice Expired", expiredSection.Text);
                Assert.Contains("resubmit a payment", expiredSection.Text);
                Assert.DoesNotContain("This invoice expired with partial payment", expiredSection.Text);
            });
            Assert.True(s.Driver.ElementDoesNotExist(By.Id("ContactLink")));
            Assert.True(s.Driver.ElementDoesNotExist(By.Id("ReceiptLink")));
            Assert.Equal(storeUrl, s.Driver.FindElement(By.Id("StoreLink")).GetAttribute("href"));

            // Expire paid partial
            s.GoToHome();
            invoiceId = s.CreateInvoice(2100, "EUR");
            s.GoToInvoiceCheckout(invoiceId);
            await Task.Delay(200);
            address = s.Driver.FindElement(By.CssSelector("#Address_BTC-CHAIN .truncate-center")).GetAttribute("data-text");
            var amountFraction = "0.00001";
            await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(address, Network.RegTest),
                Money.Parse(amountFraction));
            await s.Server.ExplorerNode.GenerateAsync(1);
            
            expirySeconds = s.Driver.FindElement(By.Id("ExpirySeconds"));
            expirySeconds.Clear();
            expirySeconds.SendKeys("3");
            s.Driver.FindElement(By.Id("Expire")).Click();

            TestUtils.Eventually(() =>
            {
                var paymentInfo = s.Driver.WaitForElement(By.Id("PaymentInfo"));
                Assert.Contains("The invoice hasn't been paid in full.", paymentInfo.Text);
                Assert.Contains("Please send", paymentInfo.Text);
            });
            TestUtils.Eventually(() =>
            {
                var expiredSection = s.Driver.FindElement(By.Id("unpaid"));
                Assert.True(expiredSection.Displayed);
                Assert.Contains("Invoice Expired", expiredSection.Text);
                Assert.Contains("This invoice expired with partial payment", expiredSection.Text);
                Assert.DoesNotContain("resubmit a payment", expiredSection.Text);
            });
            var contactLink = s.Driver.FindElement(By.Id("ContactLink"));
            Assert.Equal("Contact us", contactLink.Text);
            Assert.Matches(supportUrl.Replace("{InvoiceId}", invoiceId), contactLink.GetAttribute("href"));
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
            Assert.Contains("$", s.Driver.FindElement(By.Id("PaymentDetails-ExchangeRate")).Text);
            Assert.Contains("$", s.Driver.FindElement(By.Id("PaymentDetails-TotalFiat")).Text);
            Assert.Contains("BTC", s.Driver.FindElement(By.Id("PaymentDetails-AmountDue")).Text);
            Assert.Contains("BTC", s.Driver.FindElement(By.Id("PaymentDetails-TotalPrice")).Text);

            // Pay partial amount
            await Task.Delay(200);
            s.Driver.FindElement(By.Id("test-payment-amount")).Clear();
            s.Driver.FindElement(By.Id("test-payment-amount")).SendKeys("0.00001");
            
            // Fake Pay
            TestUtils.Eventually(() =>
            {
                s.Driver.FindElement(By.Id("FakePayment")).Click();
                s.Driver.FindElement(By.Id("mine-block")).Click();
                var paymentInfo = s.Driver.WaitForElement(By.Id("PaymentInfo"));
                Assert.Contains("The invoice hasn't been paid in full", paymentInfo.Text);
                Assert.Contains("Please send", paymentInfo.Text);
            });

            s.Driver.Navigate().Refresh();

            // Pay full amount
            s.PayInvoice();

            // Processing
            TestUtils.Eventually(() =>
            {
                var processingSection = s.Driver.WaitForElement(By.Id("processing"));
                Assert.True(processingSection.Displayed);
                Assert.Contains("Payment Received", processingSection.Text);
                Assert.Contains("Your payment has been received and is now processing", processingSection.Text);
            });
            s.Driver.FindElement(By.Id("confetti"));

            // Mine
            s.MineBlockOnInvoiceCheckout();
            TestUtils.Eventually(() =>
            {
                Assert.Contains("Mined 1 block",
                    s.Driver.WaitForElement(By.Id("CheatSuccessMessage")).Text);
            });

            // Settled
            TestUtils.Eventually(() =>
            {
                var settledSection = s.Driver.WaitForElement(By.Id("settled"));
                Assert.True(settledSection.Displayed);
                Assert.Contains("Invoice Paid", settledSection.Text);
            });
            s.Driver.FindElement(By.Id("confetti"));
            s.Driver.FindElement(By.Id("ReceiptLink"));
            Assert.True(s.Driver.ElementDoesNotExist(By.Id("ContactLink")));
            Assert.Equal(storeUrl, s.Driver.FindElement(By.Id("StoreLink")).GetAttribute("href"));

            // BIP21
            s.GoToHome();
            s.GoToStore(StoreNavPages.CheckoutAppearance);
            s.Driver.SetCheckbox(By.Id("OnChainWithLnInvoiceFallback"), true);
            s.Driver.SetCheckbox(By.Id("LightningAmountInSatoshi"), false);
            s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);

            invoiceId = s.CreateInvoice();
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            Assert.Contains("BTC", s.Driver.FindElement(By.Id("AmountDue")).Text);
            qrValue = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-qr-value");
            clipboard = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-clipboard");
            payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            var copyAddressOnchain = s.Driver.FindElement(By.CssSelector("#Address_BTC-CHAIN .truncate-center")).GetAttribute("data-text");
            var copyAddressLightning = s.Driver.FindElement(By.CssSelector("#Lightning_BTC-CHAIN .truncate-center")).GetAttribute("data-text");
            Assert.StartsWith($"bitcoin:{copyAddressOnchain}?amount=", payUrl);
            Assert.Contains("?amount=", payUrl);
            Assert.Contains("&lightning=", payUrl);
            Assert.StartsWith("bcrt", copyAddressOnchain);
            Assert.StartsWith("lnbcrt", copyAddressLightning);
            Assert.StartsWith($"bitcoin:{copyAddressOnchain.ToUpperInvariant()}?amount=", qrValue);
            Assert.Contains("&lightning=LNBCRT", qrValue);
            Assert.Contains("&lightning=lnbcrt", clipboard);
            Assert.Equal(clipboard, payUrl);

            // Check details
            s.Driver.ToggleCollapse("PaymentDetails");
            Assert.Contains("1 BTC = ", s.Driver.FindElement(By.Id("PaymentDetails-ExchangeRate")).Text);
            Assert.Contains("$", s.Driver.FindElement(By.Id("PaymentDetails-ExchangeRate")).Text);
            Assert.Contains("$", s.Driver.FindElement(By.Id("PaymentDetails-TotalFiat")).Text);
            Assert.Contains("BTC", s.Driver.FindElement(By.Id("PaymentDetails-AmountDue")).Text);
            Assert.Contains("BTC", s.Driver.FindElement(By.Id("PaymentDetails-TotalPrice")).Text);

            // Switch to amount displayed in sats
            s.GoToHome();
            s.GoToStore(StoreNavPages.CheckoutAppearance);
            s.Driver.SetCheckbox(By.Id("LightningAmountInSatoshi"), true);
            s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Contains("sats", s.Driver.FindElement(By.Id("AmountDue")).Text);

            // Check details
            s.Driver.ToggleCollapse("PaymentDetails");
            Assert.Contains("1 sat = ", s.Driver.FindElement(By.Id("PaymentDetails-ExchangeRate")).Text);
            Assert.Contains("$", s.Driver.FindElement(By.Id("PaymentDetails-ExchangeRate")).Text);
            Assert.Contains("$", s.Driver.FindElement(By.Id("PaymentDetails-TotalFiat")).Text);
            Assert.Contains("sats", s.Driver.FindElement(By.Id("PaymentDetails-AmountDue")).Text);
            Assert.Contains("sats", s.Driver.FindElement(By.Id("PaymentDetails-TotalPrice")).Text);

            // BIP21 with LN as default payment method
            s.GoToHome();
            invoiceId = s.CreateInvoice(defaultPaymentMethod: "BTC-LN");
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            Assert.StartsWith("bitcoin:", payUrl);
            Assert.Contains("&lightning=lnbcrt", payUrl);

            // Check details
            s.Driver.ToggleCollapse("PaymentDetails");
            Assert.Contains("1 sat = ", s.Driver.FindElement(By.Id("PaymentDetails-ExchangeRate")).Text);
            Assert.Contains("$", s.Driver.FindElement(By.Id("PaymentDetails-ExchangeRate")).Text);
            Assert.Contains("$", s.Driver.FindElement(By.Id("PaymentDetails-TotalFiat")).Text);
            Assert.Contains("sats", s.Driver.FindElement(By.Id("PaymentDetails-AmountDue")).Text);
            Assert.Contains("sats", s.Driver.FindElement(By.Id("PaymentDetails-TotalPrice")).Text);

            // Ensure LNURL is enabled
            s.GoToHome();
            s.GoToLightningSettings();
            Assert.True(s.Driver.FindElement(By.Id("LNURLEnabled")).Selected);

            // BIP21 with top-up invoice
            invoiceId = s.CreateInvoice(amount: null);
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            qrValue = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-qr-value");
            clipboard = s.Driver.FindElement(By.CssSelector(".qr-container")).GetAttribute("data-clipboard");
            payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            copyAddressOnchain = s.Driver.FindElement(By.CssSelector("#Address_BTC-CHAIN .truncate-center")).GetAttribute("data-text");
            copyAddressLightning = s.Driver.FindElement(By.CssSelector("#Lightning_BTC-CHAIN .truncate-center")).GetAttribute("data-text");
            Assert.StartsWith($"bitcoin:{copyAddressOnchain}", payUrl);
            Assert.Contains("?lightning=lnurl", payUrl);
            Assert.DoesNotContain("amount=", payUrl);
            Assert.StartsWith("bcrt", copyAddressOnchain);
            Assert.StartsWith("lnurl", copyAddressLightning);
            Assert.StartsWith($"bitcoin:{copyAddressOnchain.ToUpperInvariant()}?lightning=LNURL", qrValue);
            Assert.Contains($"bitcoin:{copyAddressOnchain}?lightning=lnurl", clipboard);
            Assert.Equal(clipboard, payUrl);

            // Check details
            s.Driver.ToggleCollapse("PaymentDetails");
            Assert.Contains("1 sat = ", s.Driver.FindElement(By.Id("PaymentDetails-ExchangeRate")).Text);
            Assert.Contains("$", s.Driver.FindElement(By.Id("PaymentDetails-ExchangeRate")).Text);
            s.Driver.ElementDoesNotExist(By.Id("PaymentDetails-TotalFiat"));
            s.Driver.ElementDoesNotExist(By.Id("PaymentDetails-AmountDue"));
            s.Driver.ElementDoesNotExist(By.Id("PaymentDetails-TotalPrice"));

            // Expiry message should not show amount for top-up invoice
            expirySeconds = s.Driver.FindElement(By.Id("ExpirySeconds"));
            expirySeconds.Clear();
            expirySeconds.SendKeys("5");
            s.Driver.FindElement(By.Id("Expire")).Click();
            TestUtils.Eventually(() =>
            {
                var paymentInfo = s.Driver.WaitForElement(By.Id("PaymentInfo"));
                Assert.Contains("This invoice will expire in", paymentInfo.Text);
                Assert.Contains("00:0", paymentInfo.Text);
                Assert.DoesNotContain("Please send", paymentInfo.Text);
            });

            // Configure countdown timer
            s.GoToHome();
            invoiceId = s.CreateInvoice();
            s.GoToHome();
            s.GoToStore(StoreNavPages.CheckoutAppearance);
            var displayExpirationTimer = s.Driver.FindElement(By.Id("DisplayExpirationTimer"));
            Assert.Equal("5", displayExpirationTimer.GetAttribute("value"));
            displayExpirationTimer.Clear();
            displayExpirationTimer.SendKeys("10");
            s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", s.FindAlertMessage().Text);

            s.GoToInvoiceCheckout(invoiceId);
            var paymentInfo = s.Driver.FindElement(By.Id("PaymentInfo"));
            Assert.False(paymentInfo.Displayed);
            Assert.DoesNotContain("This invoice will expire in", paymentInfo.Text);

            expirySeconds = s.Driver.FindElement(By.Id("ExpirySeconds"));
            expirySeconds.Clear();
            expirySeconds.SendKeys("599");
            s.Driver.FindElement(By.Id("Expire")).Click();
            TestUtils.Eventually(() =>
            {
                paymentInfo = s.Driver.WaitForElement(By.Id("PaymentInfo"));
                Assert.True(paymentInfo.Displayed);
                Assert.Contains("This invoice will expire in", paymentInfo.Text);
                Assert.Contains("09:5", paymentInfo.Text);
            });

            // Disable LNURL again
            s.GoToHome();
            s.GoToLightningSettings();
            s.Driver.SetCheckbox(By.Id("LNURLEnabled"), false);
            s.ClickPagePrimary();
            Assert.Contains("BTC Lightning settings successfully updated", s.FindAlertMessage().Text);

            // Test:
            // - NFC/LNURL-W available with just Lightning
            // - BIP21 works correctly even though Lightning is default payment method
            s.GoToHome();
            invoiceId = s.CreateInvoice(defaultPaymentMethod: "BTC-LN");
            s.GoToInvoiceCheckout(invoiceId);
            Assert.Empty(s.Driver.FindElements(By.CssSelector(".payment-method")));
            payUrl = s.Driver.FindElement(By.Id("PayInWallet")).GetAttribute("href");
            Assert.StartsWith("bitcoin:", payUrl);
            Assert.Contains("&lightning=lnbcrt", payUrl);

            // Language Switch
            var languageSelect = new SelectElement(s.Driver.FindElement(By.Id("DefaultLang")));
            Assert.Equal("English", languageSelect.SelectedOption.Text);
            Assert.Equal("View Details", s.Driver.FindElement(By.Id("DetailsToggle")).Text);
            Assert.DoesNotContain("lang=", s.Driver.Url);
            languageSelect.SelectByText("Deutsch");
            Assert.Equal("Details anzeigen", s.Driver.FindElement(By.Id("DetailsToggle")).Text);
            Assert.Contains("lang=de", s.Driver.Url);

            s.Driver.Navigate().Refresh();
            languageSelect = new SelectElement(s.Driver.WaitForElement(By.Id("DefaultLang")));
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
            iframe.WaitUntilAvailable(By.Id("Checkout"));

            await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(invoice
                    .GetPaymentPrompt(PaymentTypes.CHAIN.GetPaymentMethodId("BTC"))
                    .Destination, Network.RegTest),
                new Money(0.001m, MoneyUnit.BTC));

            TestUtils.Eventually(() =>
            {
                var closeButton = iframe.FindElement(By.Id("close"));
                Assert.True(closeButton.Displayed);
                closeButton.Click();
            });
            s.Driver.AssertElementNotFound(By.Name("btcpay"));
            Assert.Equal(s.Driver.Url,
                new Uri(s.ServerUri, $"tests/index.html?invoice={invoiceId}").ToString());
        }
    }
}
