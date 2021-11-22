using System;
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
    public class CheckoutUITests : UnitTestBase
    {
        public const int TestTimeout = TestUtils.TestTimeout;
        public CheckoutUITests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanHandleRefundEmailForm()
        {

            using (var s = CreateSeleniumTester())
            {
                await s.StartAsync();
                s.GoToRegister();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme("BTC");
                s.GoToStore(store.storeId, StoreNavPages.CheckoutAppearance);
                s.Driver.FindElement(By.Id("RequiresRefundEmail")).Click();
                s.Driver.FindElement(By.Name("command")).Click();

                var emailAlreadyThereInvoiceId = s.CreateInvoice(store.storeName, 100, "USD", "a@g.com");
                s.GoToInvoiceCheckout(emailAlreadyThereInvoiceId);
                s.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));
                s.GoToHome();
                var invoiceId = s.CreateInvoice(store.storeName);
                s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
                s.Driver.AssertNoError();
                s.Driver.Navigate().Back();
                s.Driver.FindElement(By.ClassName("invoice-checkout-link")).Click();
                Assert.NotEmpty(s.Driver.FindElements(By.Id("checkoutCtrl")));

                Assert.True(s.Driver.FindElement(By.Id("emailAddressFormInput")).Displayed);
                s.Driver.FindElement(By.Id("emailAddressFormInput")).SendKeys("xxx");
                s.Driver.FindElement(By.Id("emailAddressForm")).FindElement(By.CssSelector("button.action-button"))
                    .Click();
                var formInput = s.Driver.FindElement(By.Id("emailAddressFormInput"));

                Assert.True(formInput.Displayed);
                Assert.Contains("form-input-invalid", formInput.GetAttribute("class"));
                formInput = s.Driver.FindElement(By.Id("emailAddressFormInput"));
                formInput.SendKeys("@g.com");
                var actionButton = s.Driver.FindElement(By.Id("emailAddressForm")).FindElement(By.CssSelector("button.action-button"));
                actionButton.Click();
                try // Sometimes the click only take the focus, without actually really clicking on it...
                {
                    actionButton.Click();
                }
                catch { }

                s.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));
                s.Driver.Navigate().Refresh();
                s.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanHandleRefundEmailForm2()
        {

            using (var s = CreateSeleniumTester())
            {
                // Prepare user account and store
                await s.StartAsync();
                s.GoToRegister();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme("BTC");

                // Now create an invoice that requires a refund email
                var invoice = s.CreateInvoice(store.storeName, 100, "USD", "", null, true);
                s.GoToInvoiceCheckout(invoice);

                var emailInput = s.Driver.FindElement(By.Id("emailAddressFormInput"));
                Assert.True(emailInput.Displayed);

                emailInput.SendKeys("a@g.com");

                var actionButton = s.Driver.FindElement(By.Id("emailAddressForm")).FindElement(By.CssSelector("button.action-button"));
                actionButton.Click();
                try // Sometimes the click only take the focus, without actually really clicking on it...
                {
                    actionButton.Click();
                }
                catch { }

                s.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));
                s.Driver.Navigate().Refresh();
                s.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));

                s.GoToHome();

                // Now create an invoice that doesn't require a refund email
                s.CreateInvoice(store.storeName, 100, "USD", "", null, false);
                s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
                s.Driver.AssertNoError();
                s.Driver.Navigate().Back();
                s.Driver.FindElement(By.ClassName("invoice-checkout-link")).Click();
                Assert.NotEmpty(s.Driver.FindElements(By.Id("checkoutCtrl")));
                s.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));
                s.Driver.Navigate().Refresh();
                s.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));

                s.GoToHome();

                // Now create an invoice that requires refund email but already has one set, email input shouldn't show up
                s.CreateInvoice(store.storeName, 100, "USD", "a@g.com", null, true);
                s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
                s.Driver.AssertNoError();
                s.Driver.Navigate().Back();
                s.Driver.FindElement(By.ClassName("invoice-checkout-link")).Click();
                Assert.NotEmpty(s.Driver.FindElements(By.Id("checkoutCtrl")));
                s.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));
                s.Driver.Navigate().Refresh();
                s.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseLanguageDropdown()
        {
            using (var s = CreateSeleniumTester())
            {
                await s.StartAsync();
                s.GoToRegister();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme("BTC");

                var invoiceId = s.CreateInvoice(store.storeName);
                s.GoToInvoiceCheckout(invoiceId);
                Assert.True(s.Driver.FindElement(By.Id("DefaultLang")).FindElements(By.TagName("option")).Count > 1);
                var payWithTextEnglish = s.Driver.FindElement(By.Id("pay-with-text")).Text;

                var prettyDropdown = s.Driver.FindElement(By.Id("prettydropdown-DefaultLang"));
                prettyDropdown.Click();
                await Task.Delay(200);
                prettyDropdown.FindElement(By.CssSelector("[data-value=\"da-DK\"]")).Click();
                await Task.Delay(1000);
                Assert.NotEqual(payWithTextEnglish, s.Driver.FindElement(By.Id("pay-with-text")).Text);
                s.Driver.Navigate().GoToUrl(s.Driver.Url + "?lang=da-DK");

                Assert.NotEqual(payWithTextEnglish, s.Driver.FindElement(By.Id("pay-with-text")).Text);

                s.Driver.Quit();
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Lightning", "Lightning")]
        public async Task CanSetDefaultPaymentMethod()
        {
            using (var s = CreateSeleniumTester())
            {
                s.Server.ActivateLightning();
                await s.StartAsync();
                s.GoToRegister();
                s.RegisterNewUser(true);
                var store = s.CreateNewStore();
                s.AddLightningNode();
                s.AddDerivationScheme("BTC");

                var invoiceId = s.CreateInvoice(store.storeName, defaultPaymentMethod: "BTC_LightningLike");
                s.GoToInvoiceCheckout(invoiceId);

                Assert.Equal("Bitcoin (Lightning) (BTC)", s.Driver.FindElement(By.ClassName("payment__currencies")).Text);
                s.Driver.Quit();
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Lightning", "Lightning")]
        public async Task CanUseLightningSatsFeature()
        {
            using (var s = CreateSeleniumTester())
            {
                s.Server.ActivateLightning();
                await s.StartAsync();
                s.GoToRegister();
                s.RegisterNewUser(true);
                (string storeName, string storeId) = s.CreateNewStore();
                s.AddLightningNode();
                s.GoToStore(storeId);
                s.Driver.FindElement(By.Id("Modify-LightningBTC")).Click();
                s.Driver.SetCheckbox(By.Id("LightningAmountInSatoshi"), true);
                s.Driver.FindElement(By.Id("save")).Click();
                Assert.Contains("BTC Lightning settings successfully updated", s.FindAlertMessage().Text);
                
                var invoiceId = s.CreateInvoice(storeName, 10, "USD", "a@g.com");
                s.GoToInvoiceCheckout(invoiceId);
                Assert.Contains("Sats", s.Driver.FindElement(By.ClassName("payment__currencies_noborder")).Text);
            }
        }

        [Fact(Timeout = TestTimeout)]
        public async Task CanUseJSModal()
        {
            using (var s = CreateSeleniumTester())
            {
                await s.StartAsync();
                s.GoToRegister();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.GoToStore(store.storeId);
                s.AddDerivationScheme();
                var invoiceId = s.CreateInvoice(store.storeId, 0.001m, "BTC", "a@x.com");
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
                    closebutton = iframe.FindElement(By.ClassName("close-action"));
                    Assert.True(closebutton.Displayed);
                });
                closebutton.Click();
                s.Driver.AssertElementNotFound(By.Name("btcpay"));
                Assert.Equal(s.Driver.Url,
                    new Uri(s.ServerUri, $"tests/index.html?invoice={invoiceId}").ToString());
            }
        }
    }
}
