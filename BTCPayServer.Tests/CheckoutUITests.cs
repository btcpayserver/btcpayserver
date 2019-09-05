using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Tests.Logging;
using BTCPayServer.Views.Stores;
using NBitpayClient;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Trait("Selenium", "Selenium")]
    public class CheckoutUITests
    {
        public CheckoutUITests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }
       
        
        [Fact]
        public void CanCreateInvoice()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                var store = s.CreateNewStore().storeName;
                s.AddDerivationScheme();

                s.CreateInvoice(store);

                s.Driver.FindElement(By.ClassName("invoice-details-link")).Click();
                s.Driver.AssertNoError();
                s.Driver.Navigate().Back();
                s.Driver.FindElement(By.ClassName("invoice-checkout-link")).Click();
                Assert.NotEmpty(s.Driver.FindElements(By.Id("checkoutCtrl")));
                s.Driver.Quit();
            }
        }
        
        [Fact]
        public async Task CanHandleRefundEmailForm()
        {

            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme("BTC");

                var emailAlreadyThereInvoiceId =s.CreateInvoice(store.storeName, 100, "USD", "a@g.com");
                s.GoToInvoiceCheckout(emailAlreadyThereInvoiceId);
                try
                {
                    var emailInput = s.Driver.FindElement(By.Id("emailAddressFormInput"));
                    Assert.False(emailInput.Displayed);
                }
                catch (NoSuchElementException)
                {
                }
                
                s.GoToHome();
                var invoiceId = s.CreateInvoice(store.storeName);
                s.GoToInvoiceCheckout(invoiceId);
                Assert.True(s.Driver.FindElement(By.Id("emailAddressFormInput")).Displayed);
                s.Driver.FindElement(By.Id("emailAddressFormInput")).SendKeys("xxx");
                s.Driver.FindElement(By.Id("emailAddressForm")).FindElement(By.CssSelector("button.action-button"))
                    .Click();
                
                Assert.True(s.Driver.FindElement(By.Id("emailAddressFormInput")).Displayed);
                s.Driver.FindElement(By.Id("emailAddressFormInput")).SendKeys("@g.com");
                s.Driver.FindElement(By.Id("emailAddressForm")).FindElement(By.CssSelector("button.action-button"))
                    .Click();

                try
                {
                    var el = s.Driver.FindElement(By.Id("emailAddressForm"))
                        .FindElement(By.CssSelector("button.action-button"));
                    
                    while (el.Displayed && el.Enabled)
                    {
                        await Task.Delay(200);
                    }
                }
                catch (NoSuchElementException)
                {
                }
                
                s.Driver.Navigate().Refresh();
               
                try
                {
                    var emailInput = s.Driver.FindElement(By.Id("emailAddressFormInput"));
                    Assert.False(emailInput.Displayed);
                }
                catch (NoSuchElementException)
                {
                }
            }
        }

        [Fact]
        public void CanUseLanguageDropdown()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme("BTC");

                var invoiceId = s.CreateInvoice(store.storeName);
                s.GoToInvoiceCheckout(invoiceId);
                Assert.True(s.Driver.FindElement(By.Id("DefaultLang")).FindElements(By.TagName("option")).Count > 1);
                var payWithTextEnglish = s.Driver.FindElement(By.Id("pay-with-text")).Text;
                var prettyDropdown = s.Driver.FindElement(By.Id("prettydropdown-DefaultLang"));
                prettyDropdown.ForceClick(s.Driver);
                prettyDropdown.FindElement(By.CssSelector("[data-value=\"da-DK\"]")).ForceClick(s.Driver);
                Assert.NotEqual(payWithTextEnglish, s.Driver.FindElement(By.Id("pay-with-text")).Text);
                s.Driver.Navigate().GoToUrl(s.Driver.Url + "?lang=da-DK");
                
                Assert.NotEqual(payWithTextEnglish, s.Driver.FindElement(By.Id("pay-with-text")).Text);
                
                s.Driver.Quit();
            }
        }
        
        [Fact]
        public void CanUsePaymentMethodDropdown()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.AddDerivationScheme("BTC");
                
                //check that there is no dropdown since only one payment method is set
                var invoiceId = s.CreateInvoice(store.storeName, 10, "USD", "a@g.com");
                s.GoToInvoiceCheckout(invoiceId);
                s.Driver.FindElement(By.ClassName("payment__currencies_noborder"));
                s.GoToHome();
                s.GoToStore(store.storeId);
                s.AddDerivationScheme("LTC");
                s.AddLightningNode("BTC",LightningConnectionType.CLightning);
                //there should be three now
                invoiceId = s.CreateInvoice(store.storeName, 10, "USD", "a@g.com");
                s.GoToInvoiceCheckout(invoiceId);
                var currencyDropdownButton =  s.Driver.FindElement(By.ClassName("payment__currencies"));
                Assert.Contains("BTC", currencyDropdownButton.Text);
                currencyDropdownButton.Click();
                
                var elements = s.Driver.FindElement(By.ClassName("vex-content"))
                    .FindElements(By.ClassName("vexmenuitem"));
                Assert.Equal(3, elements.Count);
                elements.Single(element => element.Text.Contains("LTC")).Click();
                currencyDropdownButton =  s.Driver.FindElement(By.ClassName("payment__currencies"));
                Assert.Contains("LTC", currencyDropdownButton.Text);
                
                elements = s.Driver.FindElement(By.ClassName("vex-content"))
                    .FindElements(By.ClassName("vexmenuitem"));
                
                elements.Single(element => element.Text.Contains("Lightning")).Click();
                
                Assert.Contains("Lightning", currencyDropdownButton.Text);
                
                s.Driver.Quit();
            }
        }
        
        [Fact]
        public void CanUseLightningSatsFeature()
        {
            //uncomment after https://github.com/btcpayserver/btcpayserver/pull/1014
//            using (var s = SeleniumTester.Create())
//            {
//                s.Start();
//                s.RegisterNewUser();
//                var store = s.CreateNewStore();
//                s.AddInternalLightningNode("BTC");
//                s.GoToStore(store.storeId, StoreNavPages.Checkout);
//                s.SetCheckbox(s, "LightningAmountInSatoshi", true);
//                s.Driver.FindElement(By.Name("command")).Click();
//                var invoiceId = s.CreateInvoice(store.storeName, 10, "USD", "a@g.com");
//                s.GoToInvoiceCheckout(invoiceId);
//                Assert.Contains("Sats", s.Driver.FindElement(By.ClassName("payment__currencies_noborder")).Text);
//                
//            }
        }
    }
}
