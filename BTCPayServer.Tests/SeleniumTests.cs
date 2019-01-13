using System;
using Xunit;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace BTCPayServer.Tests
{
    public class Base
    {
        public IWebDriver Driver { get; set; }
    }

    public class Browsers : Base
    {
        public Browsers()
        {
            ChromeOptions options = new ChromeOptions();
            options.AddArguments("headless");
            options.AddArguments("window-size=1200x600");
            Driver = new ChromeDriver(Environment.CurrentDirectory, options);
        }
    }

    public class ChromeTests : Browsers
    {

        [Fact]
        public void AccessStoreRequiresLogin()
        {
            Driver.Navigate().GoToUrl("https://mainnet.demo.btcpayserver.org/");
            Assert.False(Driver.PageSource.Contains("STORES"), "Access Stores Without Login");
            Driver.Quit();
        }
        [Fact]
        public void AccessServerRequiresAdmin()
        {
            Driver.Navigate().GoToUrl("https://mainnet.demo.btcpayserver.org/");
            Assert.False(Driver.PageSource.Contains("SERVER SETTINGS"), "Third-Party Server Access");
            Driver.Quit();
        }

        [Fact]
        public void RandomUserLogin()
        {
            Random randm = new Random();
            int store_rand = randm.Next(1, 2000);

            //Register & Log Out
            Driver.Navigate().GoToUrl("https://mainnet.demo.btcpayserver.org/");
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click(); // Register H
            Driver.FindElement(By.Id("Email")).SendKeys("1@A." + store_rand);
            Driver.FindElement(By.Id("Password")).SendKeys("1234567");
            Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("1234567");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click();
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click(); // Log Out B

            //Same User Can Log Back In
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click(); // Log In H
            Driver.FindElement(By.Id("Email")).SendKeys("1@A." + store_rand);
            Driver.FindElement(By.Id("Password")).SendKeys("1234567");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Log In B

            //Change Password & Log Out
            Driver.FindElement(By.CssSelector("li.nav-item:nth-of-type(5)")).Click(); // My Settings H
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[1]/div/a[2]")).Click(); // Change Password B
            Driver.FindElement(By.Id("OldPassword")).SendKeys("1234567");
            Driver.FindElement(By.Id("NewPassword")).SendKeys("abc????");
            Driver.FindElement(By.Id("ConfirmPassword")).SendKeys("abc????");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Update PW B
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click(); // Log Out B

            //Log In With New Password
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click(); // Log In H
            Driver.FindElement(By.Id("Email")).SendKeys("1@A." + store_rand);
            Driver.FindElement(By.Id("Password")).SendKeys("abc????");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Log In B
            Assert.True(Driver.PageSource.Contains("Stores"), "Can't Access Stores");
            Driver.Quit();

        }

        [Fact]
        public void CanCreateDeleteStore()
        {
            //Default User Login
            Driver.Navigate().GoToUrl("https://mainnet.demo.btcpayserver.org/");
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click();
            Driver.FindElement(By.Id("Email")).SendKeys("Q@0.0");
            Driver.FindElement(By.Id("Password")).SendKeys("1234567");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Log In B

            //Create Store0
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.CssSelector("a.btn.btn-primary")).Click(); // Create New Store B
            Driver.FindElement(By.Id("Name")).SendKeys("Store0");
            Driver.FindElement(By.CssSelector("input.btn.btn-primary")).Click(); // Create
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click(); // Stores H
            Assert.True(Driver.PageSource.Contains("Store0"), "Unable to create Store0");

            //Delete Store0
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[3]/table/tbody/tr[1]/td[3]/a[2]")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[1]/div/a[5]")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[2]/div[2]/div/div[3]/table/tbody/tr/td[3]/a")).Click();
            Driver.FindElement(By.CssSelector("button.btn.btn-secondary.btn-danger")).Click(); // Create
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click();
            Driver.Quit();
        }

        [Fact]
        public void CanCreateInvoiceDerivT()
        {
            //Default User Login
            Driver.Navigate().GoToUrl("https://mainnet.demo.btcpayserver.org/");
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click();
            Driver.FindElement(By.Id("Email")).SendKeys("Q@0.0");
            Driver.FindElement(By.Id("Password")).SendKeys("1234567");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click();

            //Create Store1
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.CssSelector("a.btn.btn-primary")).Click(); // Create New Store B
            Driver.FindElement(By.Id("Name")).SendKeys("Store1");
            Driver.FindElement(By.CssSelector("input.btn.btn-primary")).Click(); // Create

            //Add Derivation to Store1
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[2]/div[3]/div/form/div[11]/table/tbody/tr[1]/td[4]/a")).Click();
            Driver.FindElement(By.Id("DerivationScheme")).SendKeys("zpub6nRuLGXyRu8K4p4b2T8odQpKUFqnAFVtsKNP3huVpsHPKVBgE6JyqrDM2iMA5V3CKfRgTw86uqgR1fyG2Wzy87gpJnMSQyB6TMdUi6boMDK");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Continue
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Confirm
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Save

            //Create Store Invoice
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[3]/table/tbody/tr[1]/td[3]/a[1]")).Click();
            Driver.FindElement(By.CssSelector("a.btn.btn-primary")).Click(); // Create New Invoice B
            Driver.FindElement(By.CssSelector("input#Amount.form-control")).SendKeys("100"); // Create New Invoice B
            Driver.FindElement(By.Name("StoreId")).SendKeys("Store1" + Keys.Enter);
            Driver.FindElement(By.CssSelector("input.btn.btn-primary")).Click(); // Create New Invoice B
            Assert.True(Driver.PageSource.Contains("just created!"), "Unable to create Invoice");

            //Delete Store1
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[3]/table/tbody/tr[1]/td[3]/a[2]")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[1]/div/a[5]")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[2]/div[2]/div/div[3]/table/tbody/tr/td[3]/a")).Click();
            Driver.FindElement(By.CssSelector("button.btn.btn-secondary.btn-danger")).Click(); // Create
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click();
            Driver.Quit();
        }

        [Fact]
        public void CanCreateInvoiceDerviF()
        {
            //Default User Login
            Driver.Navigate().GoToUrl("https://mainnet.demo.btcpayserver.org/");
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click();
            Driver.FindElement(By.Id("Email")).SendKeys("Q@0.0");
            Driver.FindElement(By.Id("Password")).SendKeys("1234567");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click();

            //Create Store0
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.CssSelector("a.btn.btn-primary")).Click(); // Create New Store B
            Driver.FindElement(By.Id("Name")).SendKeys("Store0");
            Driver.FindElement(By.CssSelector("input.btn.btn-primary")).Click(); // Create

            //Create Store Invoice
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[3]/table/tbody/tr/td[3]/a[1]")).Click();
            Driver.FindElement(By.CssSelector("a.btn.btn-primary")).Click(); // Create New Invoice B
            Driver.FindElement(By.CssSelector("input#Amount.form-control")).SendKeys("100"); // Create New Invoice B
            Driver.FindElement(By.Name("StoreId")).SendKeys("Store0" + Keys.Enter);
            Driver.FindElement(By.CssSelector("input.btn.btn-primary")).Click();
            Assert.True(Driver.PageSource.Contains("need to configure the derivation"), "Unable to create Store0");

            //Delete Store1
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[3]/table/tbody/tr[1]/td[3]/a[2]")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[1]/div/a[5]")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[2]/div[2]/div/div[3]/table/tbody/tr/td[3]/a")).Click();
            Driver.FindElement(By.CssSelector("button.btn.btn-secondary.btn-danger")).Click(); // Create
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click();
            Driver.Quit();
        }

        [Fact]
        public void CanCreateAppPoS()
        {
            //Default User Login
            Driver.Navigate().GoToUrl("https://mainnet.demo.btcpayserver.org/");
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click();
            Driver.FindElement(By.Id("Email")).SendKeys("Q@0.0");
            Driver.FindElement(By.Id("Password")).SendKeys("1234567");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Log In B

            //Create Store0
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.CssSelector("a.btn.btn-primary")).Click(); // Create New Store B
            Driver.FindElement(By.Id("Name")).SendKeys("Store0");
            Driver.FindElement(By.CssSelector("input.btn.btn-primary")).Click(); // Create
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click(); // Stores H

            //Create App PoS
            Driver.FindElement(By.CssSelector("li.nav-item:nth-of-type(2)")).Click(); // My Settings H
            Driver.FindElement(By.CssSelector("a.btn.btn-primary")).Click(); // Create New App B
            Driver.FindElement(By.Name("Name")).SendKeys("Store0_PoS");
            Driver.FindElement(By.CssSelector("select#SelectedAppType.form-control")).SendKeys("PointOfSale" + Keys.Enter);
            Driver.FindElement(By.CssSelector("select#SelectedStore.form-control")).SendKeys("Store0" + Keys.Enter);
            Driver.FindElement(By.CssSelector("input.btn.btn-primary")).Click();
            Driver.FindElement(By.CssSelector("input#EnableShoppingCart.form-check")).Click();
            Driver.FindElement(By.CssSelector("input.btn.btn-primary")).Click(); // Save
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[3]/table/tbody/tr/td[4]/a[2]")).Click();
            Assert.True(Driver.PageSource.Contains("Total"), "Unable to create PoS");

            //Delete Store0 & PoS
            Driver.Navigate().Back();
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[3]/table/tbody/tr[1]/td[3]/a[2]")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[1]/div/a[5]")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[2]/div[2]/div/div[3]/table/tbody/tr/td[3]/a")).Click();
            Driver.FindElement(By.CssSelector("button.btn.btn-secondary.btn-danger")).Click(); // Create
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click();
            Driver.Quit();
        }

        [Fact]
        public void CanCreateAppCF()
        {
            //Default User Login
            Driver.Navigate().GoToUrl("https://mainnet.demo.btcpayserver.org/");
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click();
            Driver.FindElement(By.Id("Email")).SendKeys("Q@0.0");
            Driver.FindElement(By.Id("Password")).SendKeys("1234567");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Log In B

            //Create Store1
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.CssSelector("a.btn.btn-primary")).Click(); // Create New App B
            Driver.FindElement(By.Id("Name")).SendKeys("Store1");
            Driver.FindElement(By.CssSelector("input.btn.btn-primary")).Click(); // Create
            //Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click(); // Stores H

            //Add Derivation to Store1
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[2]/div[3]/div/form/div[11]/table/tbody/tr[1]/td[4]/a")).Click();
            Driver.FindElement(By.Id("DerivationScheme")).SendKeys("zpub6nRuLGXyRu8K4p4b2T8odQpKUFqnAFVtsKNP3huVpsHPKVBgE6JyqrDM2iMA5V3CKfRgTw86uqgR1fyG2Wzy87gpJnMSQyB6TMdUi6boMDK");
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Continue
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Confirm
            Driver.FindElement(By.CssSelector("button.btn.btn-primary")).Click(); // Save

            //Create App CF
            Driver.FindElement(By.CssSelector("li.nav-item:nth-of-type(2)")).Click(); // My Settings H
            Driver.FindElement(By.CssSelector("a.btn.btn-primary")).Click(); // Create New App B
            Driver.FindElement(By.Name("Name")).SendKeys("Store1_CF");
            Driver.FindElement(By.CssSelector("select#SelectedAppType.form-control")).SendKeys("Crowdfund" + Keys.Enter);
            Driver.FindElement(By.CssSelector("select#SelectedStore.form-control")).SendKeys("Store1" + Keys.Enter);
            Driver.FindElement(By.CssSelector("input.btn.btn-primary")).Click();

            //Options & View
            Driver.FindElement(By.Id("Title")).SendKeys("Kukkstarter");
            Driver.FindElement(By.CssSelector("div.note-editable.card-block")).SendKeys("1BTC = 1BTC");
            Driver.FindElement(By.Id("TargetCurrency")).SendKeys("JPY");
            Driver.FindElement(By.Id("TargetAmount")).SendKeys("700");
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div[2]/div[3]/div/form/div[25]/input")).Click();
            Driver.FindElement(By.CssSelector("li.nav-item:nth-of-type(2)")).Click(); // My Settings H
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[3]/table/tbody/tr[1]/td[4]/a[2]")).Click();
            Assert.True(Driver.PageSource.Contains("Currently Active!"), "Unable to create CF");

            //Delete Store & CF
            Driver.Navigate().Back();
            Driver.FindElement(By.CssSelector("li.nav-item:first-child")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[3]/table/tbody/tr[1]/td[3]/a[2]")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[1]/div/a[5]")).Click();
            Driver.FindElement(By.XPath("//*[@id='page-top']/section/div/div[2]/div/div[2]/div[2]/div/div[3]/table/tbody/tr/td[3]/a")).Click();
            Driver.FindElement(By.CssSelector("button.btn.btn-secondary.btn-danger")).Click(); // Create
            Driver.FindElement(By.CssSelector("li.nav-item:last-child")).Click();
            Driver.Quit();
        }
    }
}
