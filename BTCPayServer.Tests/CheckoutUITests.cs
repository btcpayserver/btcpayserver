using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Stores;
using NBitpayClient;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    [Collection("Selenium collection")]
    public class CheckoutUITests
    {
        public SeleniumTester SeleniumTester { get; }

        public CheckoutUITests(ITestOutputHelper helper, SeleniumTester seleniumTester)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
            SeleniumTester = seleniumTester;
        }


        [Fact]
        public void CanCreateInvoice()
        {
            SeleniumTester.RegisterNewUser();
            var store = SeleniumTester.CreateNewStore().storeName;
            SeleniumTester.AddDerivationScheme();

            SeleniumTester.CreateInvoice(store);

            SeleniumTester.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
            SeleniumTester.Driver.AssertNoError();
            SeleniumTester.Driver.Navigate().Back();
            SeleniumTester.Driver.FindElement(By.ClassName("invoice-checkout-link")).Click();
            Assert.NotEmpty(SeleniumTester.Driver.FindElements(By.Id("checkoutCtrl")));
        }

        [Fact]
        public async Task CanHandleRefundEmailForm()
        {
            SeleniumTester.RegisterNewUser();
            var store = SeleniumTester.CreateNewStore();
            SeleniumTester.AddDerivationScheme("BTC");

            var emailAlreadyThereInvoiceId = SeleniumTester.CreateInvoice(store.storeName, 100, "USD", "a@g.com");
            SeleniumTester.GoToInvoiceCheckout(emailAlreadyThereInvoiceId);
            SeleniumTester.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));
            SeleniumTester.GoToHome();
            var invoiceId = SeleniumTester.CreateInvoice(store.storeName);
            SeleniumTester.GoToInvoiceCheckout(invoiceId);
            Assert.True(SeleniumTester.Driver.FindElement(By.Id("emailAddressFormInput")).Displayed);
            SeleniumTester.Driver.FindElement(By.Id("emailAddressFormInput")).SendKeys("xxx");
            SeleniumTester.Driver.FindElement(By.Id("emailAddressForm")).FindElement(By.CssSelector("button.action-button"))
                .Click();

            Assert.True(SeleniumTester.Driver.FindElement(By.Id("emailAddressFormInput")).Displayed);
            SeleniumTester.Driver.FindElement(By.Id("emailAddressFormInput")).SendKeys("@g.com");
            SeleniumTester.Driver.FindElement(By.Id("emailAddressForm")).FindElement(By.CssSelector("button.action-button"))
                .Click();

            await Task.Delay(1000);
            SeleniumTester.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));

            SeleniumTester.Driver.Navigate().Refresh();

            SeleniumTester.Driver.AssertElementNotFound(By.Id("emailAddressFormInput"));
        }

        [Fact]
        public async Task CanUseLanguageDropdown()
        {
            SeleniumTester.RegisterNewUser();
            var store = SeleniumTester.CreateNewStore();
            SeleniumTester.AddDerivationScheme("BTC");

            var invoiceId = SeleniumTester.CreateInvoice(store.storeName);
            SeleniumTester.GoToInvoiceCheckout(invoiceId);
            Assert.True(SeleniumTester.Driver.FindElement(By.Id("DefaultLang")).FindElements(By.TagName("option")).Count > 1);
            var payWithTextEnglish = SeleniumTester.Driver.FindElement(By.Id("pay-with-text")).Text;

            var prettyDropdown = SeleniumTester.Driver.FindElement(By.Id("prettydropdown-DefaultLang"));
            prettyDropdown.Click();
            await Task.Delay(200);
            prettyDropdown.FindElement(By.CssSelector("[data-value=\"da-DK\"]")).Click();
            await Task.Delay(1000);
            Assert.NotEqual(payWithTextEnglish, SeleniumTester.Driver.FindElement(By.Id("pay-with-text")).Text);
            SeleniumTester.Driver.Navigate().GoToUrl(SeleniumTester.Driver.Url + "?lang=da-DK");

            Assert.NotEqual(payWithTextEnglish, SeleniumTester.Driver.FindElement(By.Id("pay-with-text")).Text);
        }

        [Fact]
        public void CanUsePaymentMethodDropdown()
        {
            SeleniumTester.RegisterNewUser();
            var store = SeleniumTester.CreateNewStore();
            SeleniumTester.AddDerivationScheme("BTC");

            //check that there is no dropdown since only one payment method is set
            var invoiceId = SeleniumTester.CreateInvoice(store.storeName, 10, "USD", "a@g.com");
            SeleniumTester.GoToInvoiceCheckout(invoiceId);
            SeleniumTester.Driver.FindElement(By.ClassName("payment__currencies_noborder"));
            SeleniumTester.GoToHome();
            SeleniumTester.GoToStore(store.storeId);
            SeleniumTester.AddDerivationScheme("LTC");
            SeleniumTester.AddLightningNode("BTC", LightningConnectionType.CLightning);
            //there should be three now
            invoiceId = SeleniumTester.CreateInvoice(store.storeName, 10, "USD", "a@g.com");
            SeleniumTester.GoToInvoiceCheckout(invoiceId);
            var currencyDropdownButton = SeleniumTester.Driver.FindElement(By.ClassName("payment__currencies"));
            Assert.Contains("BTC", currencyDropdownButton.Text);
            currencyDropdownButton.Click();

            var elements = SeleniumTester.Driver.FindElement(By.ClassName("vex-content"))
                .FindElements(By.ClassName("vexmenuitem"));
            Assert.Equal(3, elements.Count);
            elements.Single(element => element.Text.Contains("LTC")).Click();
            currencyDropdownButton = SeleniumTester.Driver.FindElement(By.ClassName("payment__currencies"));
            Assert.Contains("LTC", currencyDropdownButton.Text);

            elements = SeleniumTester.Driver.FindElement(By.ClassName("vex-content"))
                .FindElements(By.ClassName("vexmenuitem"));

            elements.Single(element => element.Text.Contains("Lightning")).Click();

            Assert.Contains("Lightning", currencyDropdownButton.Text);
        }

        [Fact]
        public void CanUseLightningSatsFeature()
        {
            SeleniumTester.RegisterNewUser();
            var store = SeleniumTester.CreateNewStore();
            SeleniumTester.AddInternalLightningNode("BTC");
            SeleniumTester.GoToStore(store.storeId, StoreNavPages.Checkout);
            SeleniumTester.SetCheckbox(SeleniumTester, "LightningAmountInSatoshi", true);
            var command = SeleniumTester.Driver.FindElement(By.Name("command"));

            command.ForceClick();
            var invoiceId = SeleniumTester.CreateInvoice(store.storeName, 10, "USD", "a@g.com");
            SeleniumTester.GoToInvoiceCheckout(invoiceId);
            Assert.Contains("Sats", SeleniumTester.Driver.FindElement(By.ClassName("payment__currencies_noborder")).Text);
        }
    }
}
