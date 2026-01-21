using System;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Views.Stores;
using NBitcoin;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Playwright", "Playwright-2")]
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
            await using var s = CreatePlaywrightTester();
            s.Server.ActivateLightning();
            await s.StartAsync();
            await s.RegisterNewUser(true);
            await s.CreateNewStore();
            await s.AddLightningNode();
            // Use non-legacy derivation scheme
            await s.AddDerivationScheme("BTC", "tpubDD79XF4pzhmPSJ9AyUay9YbXAeD1c6nkUqC32pnKARJH6Ja5hGUfGc76V82ahXpsKqN6UcSGXMkzR34aZq4W23C6DAdZFaVrzWqzj24F8BC");

            // Configure store url
            var storeUrl = "https://satoshisteaks.com/";
            var supportUrl = "https://support.satoshisteaks.com/{InvoiceId}/";
            await s.GoToStore();
            await s.Page.FillAsync("#StoreWebsite", storeUrl);
            await s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", await (await s.FindAlertMessage()).TextContentAsync());

            await s.GoToStore(StoreNavPages.CheckoutAppearance);
            await s.Page.ClickAsync("#Presets");
            await s.Page.ClickAsync("#Presets_InStore");
            await s.Page.Locator("#ShowPayInWalletButton").SetCheckedAsync(true);
            await s.Page.FillAsync("#SupportUrl", supportUrl);
            await s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", await (await s.FindAlertMessage()).TextContentAsync());

            // Top up/zero amount invoices
            var invoiceId = await s.CreateInvoice(amount: null);
            await s.GoToInvoiceCheckout(invoiceId);
            await s.Page.Locator(".payment-method").First.WaitForAsync();
            Assert.Equal(2, await s.Page.Locator(".payment-method").CountAsync());
            await Expect(s.Page.Locator(".payment-method.active")).ToContainTextAsync("Bitcoin");
            await Expect(s.Page.Locator(".payment-method:nth-child(2)")).ToContainTextAsync("LNURL");
            var qrValue = await s.Page.Locator(".qr-container").GetAttributeAsync("data-qr-value");
            var clipboard = await s.Page.Locator(".qr-container").GetAttributeAsync("data-clipboard");
            var payUrl = await s.Page.Locator("#PayInWallet").GetAttributeAsync("href");
            var address = await s.Page.Locator("#Address_BTC-CHAIN .truncate-center").GetAttributeAsync("data-text");
            Assert.StartsWith("bcrt", address);
            Assert.DoesNotContain("lightning=", payUrl);
            Assert.Equal($"bitcoin:{address}", payUrl);
            Assert.Equal($"bitcoin:{address}", clipboard);
            Assert.Equal($"bitcoin:{address.ToUpperInvariant()}", qrValue);
            await s.ElementDoesNotExist("#Lightning_BTC-CHAIN");

            // Contact option
            var contactLink = s.Page.Locator("#ContactLink");
            Assert.Equal("Contact us", await contactLink.TextContentAsync());
            Assert.Matches(supportUrl.Replace("{InvoiceId}", invoiceId), await contactLink.GetAttributeAsync("href"));

            // Details should show exchange rate
            await s.ToggleCollapse("PaymentDetails");
            await s.ElementDoesNotExist("#PaymentDetails-TotalPrice");
            await s.ElementDoesNotExist("#PaymentDetails-TotalFiat");
            await s.ElementDoesNotExist("#PaymentDetails-AmountDue");
            await Expect(s.Page.Locator("#PaymentDetails-ExchangeRate")).ToContainTextAsync("$");
            await Expect(s.Page.Locator("#PaymentDetails-RecommendedFee")).ToContainTextAsync("sat/byte");

            // Switch to LNURL
            await s.Page.ClickAsync(".payment-method:nth-child(2)");
            await TestUtils.EventuallyAsync(async () =>
            {
                payUrl = await s.Page.Locator("#PayInWallet").GetAttributeAsync("href");
                Assert.StartsWith("lightning:lnurl", payUrl);
                Assert.StartsWith("lnurl", await s.Page.Locator("#Lightning_BTC-CHAIN .truncate-center").GetAttributeAsync("data-text"));
                await s.ElementDoesNotExist("#Address_BTC-CHAIN");
            });

            // Default payment method
            await s.GoToHome();
            invoiceId = await s.CreateInvoice(21000, "SATS", defaultPaymentMethod: "BTC-LN");
            await s.GoToInvoiceCheckout(invoiceId);
            await s.Page.Locator(".payment-method.active").WaitForAsync();
            Assert.Equal(2, await s.Page.Locator(".payment-method").CountAsync());
            await Expect(s.Page.Locator(".payment-method.active")).ToContainTextAsync("Lightning");
            await Expect(s.Page.Locator(".payment-method").First).ToContainTextAsync("Bitcoin");
            qrValue = await s.Page.Locator(".qr-container").GetAttributeAsync("data-qr-value");
            clipboard = await s.Page.Locator(".qr-container").GetAttributeAsync("data-clipboard");
            payUrl = await s.Page.Locator("#PayInWallet").GetAttributeAsync("href");
            address = await s.Page.Locator("#Lightning_BTC-LN .truncate-center").GetAttributeAsync("data-text");
            Assert.Equal($"lightning:{address}", payUrl);
            Assert.Equal($"lightning:{address}", clipboard);
            Assert.Equal($"lightning:{address.ToUpperInvariant()}", qrValue);
            await s.ElementDoesNotExist("#Address_BTC-CHAIN");

            // Lightning amount in sats
            await Expect(s.Page.Locator("#AmountDue")).ToContainTextAsync("BTC");
            await s.GoToHome();
            await s.GoToLightningSettings();
            await s.Page.Locator("#LightningAmountInSatoshi").SetCheckedAsync(true);
            await s.ClickPagePrimary();
            Assert.Contains("BTC Lightning settings successfully updated", await (await s.FindAlertMessage()).TextContentAsync());
            await s.GoToInvoiceCheckout(invoiceId);
            await Expect(s.Page.Locator("#AmountDue")).ToContainTextAsync("sats");

            // Details should not show exchange rate
            await s.ToggleCollapse("PaymentDetails");
            await s.ElementDoesNotExist("#PaymentDetails-ExchangeRate");
            await s.ElementDoesNotExist("#PaymentDetails-TotalFiat");
            await s.ElementDoesNotExist("#PaymentDetails-RecommendedFee");
            await Expect(s.Page.Locator("#PaymentDetails-AmountDue")).ToContainTextAsync("21 000 sats");
            await Expect(s.Page.Locator("#PaymentDetails-TotalPrice")).ToContainTextAsync("21 000 sats");

            // Expire
            var expirySeconds = s.Page.Locator("#ExpirySeconds");
            await expirySeconds.ClearAsync();
            await expirySeconds.FillAsync("3");
            await s.Page.ClickAsync("#Expire");

            await TestUtils.EventuallyAsync(async () =>
            {
                var paymentInfo = s.Page.Locator("#PaymentInfo");
                await paymentInfo.WaitForAsync();
                var paymentInfoText = await paymentInfo.TextContentAsync();
                Assert.Contains("This invoice will expire in", paymentInfoText);
                Assert.DoesNotContain("Please send", paymentInfoText);
            });
            await TestUtils.EventuallyAsync(async () =>
            {
                var expiredSection = s.Page.Locator("#unpaid");
                Assert.True(await expiredSection.IsVisibleAsync());
                var expiredText = await expiredSection.TextContentAsync();
                Assert.Contains("Invoice Expired", expiredText);
                Assert.Contains("resubmit a payment", expiredText);
                Assert.DoesNotContain("This invoice expired with partial payment", expiredText);
            });
            Assert.True(await s.ElementDoesNotExist("#ReceiptLink"));
            Assert.Equal(storeUrl, await s.Page.Locator("#StoreLink").GetAttributeAsync("href"));

            // Expire paid partial
            await s.GoToHome();
            invoiceId = await s.CreateInvoice(2100, "EUR");
            await s.GoToInvoiceCheckout(invoiceId);
            await Task.Delay(200);
            address = await s.Page.Locator("#Address_BTC-CHAIN .truncate-center").GetAttributeAsync("data-text");
            var amountFraction = "0.00001";
            await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(address, Network.RegTest),
                Money.Parse(amountFraction));
            await s.Server.ExplorerNode.GenerateAsync(1);

            expirySeconds = s.Page.Locator("#ExpirySeconds");
            await expirySeconds.ClearAsync();
            await expirySeconds.FillAsync("3");
            await s.Page.ClickAsync("#Expire");

            await TestUtils.EventuallyAsync(async () =>
            {
                var paymentInfo = s.Page.Locator("#PaymentInfo");
                await paymentInfo.WaitForAsync();
                var paymentInfoText = await paymentInfo.TextContentAsync();
                Assert.Contains("The invoice hasn't been paid in full.", paymentInfoText);
                Assert.Contains("Please send", paymentInfoText);
            });
            await TestUtils.EventuallyAsync(async () =>
            {
                var expiredSection = s.Page.Locator("#unpaid");
                Assert.True(await expiredSection.IsVisibleAsync());
                var expiredText = await expiredSection.TextContentAsync();
                Assert.Contains("Invoice Expired", expiredText);
                Assert.Contains("This invoice expired with partial payment", expiredText);
                Assert.DoesNotContain("resubmit a payment", expiredText);
            });
            Assert.True(await s.ElementDoesNotExist("#ReceiptLink"));
            Assert.Equal(storeUrl, await s.Page.Locator("#StoreLink").GetAttributeAsync("href"));

            // Test payment
            await s.GoToHome();
            invoiceId = await s.CreateInvoice();
            await s.GoToInvoiceCheckout(invoiceId);

            // Details
            await s.ToggleCollapse("PaymentDetails");
            var details = s.Page.Locator(".payment-details");
            var detailsText = await details.TextContentAsync();
            Assert.Contains("Total Price", detailsText);
            Assert.Contains("Total Fiat", detailsText);
            Assert.Contains("Exchange Rate", detailsText);
            Assert.Contains("Amount Due", detailsText);
            Assert.Contains("Recommended Fee", detailsText);
            await Expect(s.Page.Locator("#PaymentDetails-ExchangeRate")).ToContainTextAsync("$");
            await Expect(s.Page.Locator("#PaymentDetails-TotalFiat")).ToContainTextAsync("$");
            await Expect(s.Page.Locator("#PaymentDetails-AmountDue")).ToContainTextAsync("BTC");
            await Expect(s.Page.Locator("#PaymentDetails-TotalPrice")).ToContainTextAsync("BTC");

            // Pay partial amount
            await Task.Delay(200);
            await s.Page.Locator("#test-payment-amount").ClearAsync();
            await s.Page.FillAsync("#test-payment-amount", "0.00001");

            // Fake Pay
            await TestUtils.EventuallyAsync(async () =>
            {
                await s.Page.ClickAsync("#FakePayment");
                await s.Page.ClickAsync("#mine-block");
                var paymentInfo = s.Page.Locator("#PaymentInfo");
                await paymentInfo.WaitForAsync();
                var paymentInfoText = await paymentInfo.TextContentAsync();
                Assert.Contains("The invoice hasn't been paid in full", paymentInfoText);
                Assert.Contains("Please send", paymentInfoText);
            });

            await s.Page.ReloadAsync();

            // Pay full amount
            await s.PayInvoice();

            // Processing
            await TestUtils.EventuallyAsync(async () =>
            {
                var processingSection = s.Page.Locator("#processing");
                await processingSection.WaitForAsync();
                Assert.True(await processingSection.IsVisibleAsync());
                var processingText = await processingSection.TextContentAsync();
                Assert.Contains("Payment Received", processingText);
                Assert.Contains("Your payment has been received and is now processing", processingText);
            });
            await s.Page.Locator("#confetti").WaitForAsync();

            // Mine
            await s.MineBlockOnInvoiceCheckout();
            await TestUtils.EventuallyAsync(async () =>
            {
                Assert.Contains("Mined 1 block",
                    await s.Page.Locator("#CheatSuccessMessage").TextContentAsync());
            });

            // Settled
            await TestUtils.EventuallyAsync(async () =>
            {
                var settledSection = s.Page.Locator("#settled");
                await settledSection.WaitForAsync();
                Assert.True(await settledSection.IsVisibleAsync());
                Assert.Contains("Invoice Paid", await settledSection.TextContentAsync());
            });
            await s.Page.Locator("#confetti").WaitForAsync();
            await s.Page.Locator("#ReceiptLink").WaitForAsync();
            Assert.Equal(storeUrl, await s.Page.Locator("#StoreLink").GetAttributeAsync("href"));

            // BIP21
            await s.GoToHome();
            await s.GoToStore(s.StoreId, StoreNavPages.CheckoutAppearance);
            await s.Page.Locator("#OnChainWithLnInvoiceFallback").SetCheckedAsync(true);
            await s.Page.Locator("#LightningAmountInSatoshi").SetCheckedAsync(false);
            await s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", await (await s.FindAlertMessage()).TextContentAsync());

            invoiceId = await s.CreateInvoice();
            await s.GoToInvoiceCheckout(invoiceId);
            async Task AssertNoPaymentMethods() => Assert.False(await s.Page.Locator(".payment-method").IsVisibleAsync());
            await AssertNoPaymentMethods();
            await Expect(s.Page.Locator("#AmountDue")).ToContainTextAsync("BTC");
            qrValue = await s.Page.Locator(".qr-container").GetAttributeAsync("data-qr-value");
            clipboard = await s.Page.Locator(".qr-container").GetAttributeAsync("data-clipboard");
            payUrl = await s.Page.Locator("#PayInWallet").GetAttributeAsync("href");
            var copyAddressOnchain = await s.Page.Locator("#Address_BTC-CHAIN .truncate-center").GetAttributeAsync("data-text");
            var copyAddressLightning = await s.Page.Locator("#Lightning_BTC-CHAIN .truncate-center").GetAttributeAsync("data-text");
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
            await s.ToggleCollapse("PaymentDetails");
            await Expect(s.Page.Locator("#PaymentDetails-ExchangeRate")).ToContainTextAsync("1 BTC = ");
            await Expect(s.Page.Locator("#PaymentDetails-ExchangeRate")).ToContainTextAsync("$");
            await Expect(s.Page.Locator("#PaymentDetails-TotalFiat")).ToContainTextAsync("$");
            await Expect(s.Page.Locator("#PaymentDetails-AmountDue")).ToContainTextAsync("BTC");
            await Expect(s.Page.Locator("#PaymentDetails-TotalPrice")).ToContainTextAsync("BTC");

            // Switch to amount displayed in sats
            await s.GoToHome();
            await s.GoToStore(s.StoreId, StoreNavPages.CheckoutAppearance);
            await s.Page.Locator("#LightningAmountInSatoshi").SetCheckedAsync(true);
            await s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", await (await s.FindAlertMessage()).TextContentAsync());
            await s.GoToInvoiceCheckout(invoiceId);
            await Expect(s.Page.Locator("#AmountDue")).ToContainTextAsync("sats");

            // Check details
            await s.ToggleCollapse("PaymentDetails");
            await Expect(s.Page.Locator("#PaymentDetails-ExchangeRate")).ToContainTextAsync("1 sat = ");
            await Expect(s.Page.Locator("#PaymentDetails-ExchangeRate")).ToContainTextAsync("$");
            await Expect(s.Page.Locator("#PaymentDetails-TotalFiat")).ToContainTextAsync("$");
            await Expect(s.Page.Locator("#PaymentDetails-AmountDue")).ToContainTextAsync("sats");
            await Expect(s.Page.Locator("#PaymentDetails-TotalPrice")).ToContainTextAsync("sats");

            // BIP21 with LN as default payment method
            await s.GoToHome();
            invoiceId = await s.CreateInvoice(defaultPaymentMethod: "BTC-LN");
            await s.GoToInvoiceCheckout(invoiceId);
            await AssertNoPaymentMethods();
            payUrl = await s.Page.Locator("#PayInWallet").GetAttributeAsync("href");
            Assert.StartsWith("bitcoin:", payUrl);
            Assert.Contains("&lightning=lnbcrt", payUrl);

            // Check details
            await s.ToggleCollapse("PaymentDetails");
            await Expect(s.Page.Locator("#PaymentDetails-ExchangeRate")).ToContainTextAsync("1 sat = ");
            await Expect(s.Page.Locator("#PaymentDetails-ExchangeRate")).ToContainTextAsync("$");
            await Expect(s.Page.Locator("#PaymentDetails-TotalFiat")).ToContainTextAsync("$");
            await Expect(s.Page.Locator("#PaymentDetails-AmountDue")).ToContainTextAsync("sats");
            await Expect(s.Page.Locator("#PaymentDetails-TotalPrice")).ToContainTextAsync("sats");

            // Ensure LNURL is enabled
            await s.GoToHome();
            await s.GoToLightningSettings();
            Assert.True(await s.Page.Locator("#LNURLEnabled").IsCheckedAsync());

            // BIP21 with top-up invoice
            invoiceId = await s.CreateInvoice(amount: null);
            await s.GoToInvoiceCheckout(invoiceId);
            await AssertNoPaymentMethods();
            qrValue = await s.Page.Locator(".qr-container").GetAttributeAsync("data-qr-value");
            clipboard = await s.Page.Locator(".qr-container").GetAttributeAsync("data-clipboard");
            payUrl = await s.Page.Locator("#PayInWallet").GetAttributeAsync("href");
            copyAddressOnchain = await s.Page.Locator("#Address_BTC-CHAIN .truncate-center").GetAttributeAsync("data-text");
            copyAddressLightning = await s.Page.Locator("#Lightning_BTC-CHAIN .truncate-center").GetAttributeAsync("data-text");
            Assert.StartsWith($"bitcoin:{copyAddressOnchain}", payUrl);
            Assert.Contains("?lightning=lnurl", payUrl);
            Assert.DoesNotContain("amount=", payUrl);
            Assert.StartsWith("bcrt", copyAddressOnchain);
            Assert.StartsWith("lnurl", copyAddressLightning);
            Assert.StartsWith($"bitcoin:{copyAddressOnchain.ToUpperInvariant()}?lightning=LNURL", qrValue);
            Assert.Contains($"bitcoin:{copyAddressOnchain}?lightning=lnurl", clipboard);
            Assert.Equal(clipboard, payUrl);

            // Check details
            await s.ToggleCollapse("PaymentDetails");
            await Expect(s.Page.Locator("#PaymentDetails-ExchangeRate")).ToContainTextAsync("1 sat = ");
            await Expect(s.Page.Locator("#PaymentDetails-ExchangeRate")).ToContainTextAsync("$");
            await s.ElementDoesNotExist("#PaymentDetails-TotalFiat");
            await s.ElementDoesNotExist("#PaymentDetails-AmountDue");
            await s.ElementDoesNotExist("#PaymentDetails-TotalPrice");

            // Expiry message should not show amount for top-up invoice
            expirySeconds = s.Page.Locator("#ExpirySeconds");
            await expirySeconds.ClearAsync();
            await expirySeconds.FillAsync("5");
            await s.Page.ClickAsync("#Expire");
            await TestUtils.EventuallyAsync(async () =>
            {
                var paymentInfo = s.Page.Locator("#PaymentInfo");
                await paymentInfo.WaitForAsync();
                var paymentInfoText = await paymentInfo.TextContentAsync();
                Assert.Contains("This invoice will expire in", paymentInfoText);
                Assert.Contains("00:0", paymentInfoText);
                Assert.DoesNotContain("Please send", paymentInfoText);
            });

            // Configure countdown timer
            await s.GoToHome();
            invoiceId = await s.CreateInvoice();
            await s.GoToHome();
            await s.GoToStore(s.StoreId, StoreNavPages.CheckoutAppearance);
            var displayExpirationTimer = s.Page.Locator("#DisplayExpirationTimer");
            Assert.Equal("5", await displayExpirationTimer.InputValueAsync());
            await displayExpirationTimer.ClearAsync();
            await displayExpirationTimer.FillAsync("10");
            await s.ClickPagePrimary();
            Assert.Contains("Store successfully updated", await (await s.FindAlertMessage()).TextContentAsync());

            await s.GoToInvoiceCheckout(invoiceId);
            var paymentInfo2 = s.Page.Locator("#PaymentInfo");
            Assert.False(await paymentInfo2.IsVisibleAsync());
            var paymentInfoText2 = await paymentInfo2.TextContentAsync();
            Assert.DoesNotContain("This invoice will expire in", paymentInfoText2);

            expirySeconds = s.Page.Locator("#ExpirySeconds");
            await expirySeconds.ClearAsync();
            await expirySeconds.FillAsync("599");
            await s.Page.ClickAsync("#Expire");
            await TestUtils.EventuallyAsync(async () =>
            {
                var paymentInfo = s.Page.Locator("#PaymentInfo");
                await paymentInfo.WaitForAsync();
                Assert.True(await paymentInfo.IsVisibleAsync());
                var paymentInfoText = await paymentInfo.TextContentAsync();
                Assert.Contains("This invoice will expire in", paymentInfoText);
                Assert.Contains("09:5", paymentInfoText);
            });

            // Disable LNURL again
            await s.GoToHome();
            await s.GoToLightningSettings();
            await s.Page.Locator("#LNURLEnabled").SetCheckedAsync(false);
            await s.ClickPagePrimary();
            Assert.Contains("BTC Lightning settings successfully updated", await (await s.FindAlertMessage()).TextContentAsync());

            // Test:
            // - NFC/LNURL-W available with just Lightning
            // - BIP21 works correctly even though Lightning is default payment method
            await s.GoToHome();
            invoiceId = await s.CreateInvoice(defaultPaymentMethod: "BTC-LN");
            await s.GoToInvoiceCheckout(invoiceId);
            await AssertNoPaymentMethods();
            payUrl = await s.Page.Locator("#PayInWallet").GetAttributeAsync("href");
            Assert.StartsWith("bitcoin:", payUrl);
            Assert.Contains("&lightning=lnbcrt", payUrl);

            // Language Switch
            var languageSelect = s.Page.Locator("#DefaultLang");
            Assert.Equal("English", (await languageSelect.Locator("option:checked").TextContentAsync()).Trim());
            Assert.Equal("View Details", (await s.Page.Locator("#DetailsToggle").TextContentAsync()).Trim());
            Assert.DoesNotContain("lang=", s.Page.Url);
            await languageSelect.SelectOptionAsync(new SelectOptionValue { Label = "Deutsch" });
            await TestUtils.EventuallyAsync(async () =>
            {
                Assert.Contains("lang=de", s.Page.Url);
                Assert.Equal("Details anzeigen", (await s.Page.Locator("#DetailsToggle").TextContentAsync()).Trim());
            });

            await s.Page.ReloadAsync();
            languageSelect = s.Page.Locator("#DefaultLang");
            Assert.Equal("Deutsch", (await languageSelect.Locator("option:checked").TextContentAsync()).Trim());
            Assert.Equal("Details anzeigen", (await s.Page.Locator("#DetailsToggle").TextContentAsync()).Trim());
            await languageSelect.SelectOptionAsync(new SelectOptionValue { Label = "English" });
            await TestUtils.EventuallyAsync(async () =>
            {
                Assert.Contains("lang=en", s.Page.Url);
                Assert.Equal("View Details", (await s.Page.Locator("#DetailsToggle").TextContentAsync()).Trim());
            });
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseCheckoutAsModal()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.RegisterNewUser();
            await s.CreateNewStore();
            await s.GoToStore();
            await s.AddDerivationScheme();
            var invoiceId = await s.CreateInvoice(0.001m, "BTC", "a@x.com");
            var invoice = await s.Server.PayTester.InvoiceRepository.GetInvoice(invoiceId);
            await s.GoToUrl($"/tests/index.html?invoice={invoiceId}");
            await s.Page.Locator("[name='btcpay']").WaitForAsync();

            var frameElement = s.Page.FrameLocator("[name='btcpay']");
            Assert.True(await s.Page.Locator("[name='btcpay']").IsVisibleAsync());
            await frameElement.Locator("#Checkout").WaitForAsync();

            await s.Server.ExplorerNode.SendToAddressAsync(BitcoinAddress.Create(invoice
                    .GetPaymentPrompt(PaymentTypes.CHAIN.GetPaymentMethodId("BTC"))
                    .Destination, Network.RegTest),
                new Money(0.001m, MoneyUnit.BTC));

            await TestUtils.EventuallyAsync(async () =>
            {
                var closeButton = frameElement.Locator("#close");
                Assert.True(await closeButton.IsVisibleAsync());
                await closeButton.ClickAsync();
            });
            await Expect(s.Page.Locator("[name='btcpay']")).Not.ToBeVisibleAsync();
            Assert.Equal(s.Page.Url,
                new Uri(s.ServerUri, $"tests/index.html?invoice={invoiceId}").ToString());
        }
    }
}
