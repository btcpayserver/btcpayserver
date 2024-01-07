using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.Extensions;
using OpenQA.Selenium.Support.UI;
using Xunit;

namespace BTCPayServer.Tests
{
    public static class Extensions
    {
        public static Task<KeyPathInformation> ReserveAddressAsync(this BTCPayWallet wallet, DerivationStrategyBase derivationStrategyBase)
        {
            return wallet.ReserveAddressAsync(null, derivationStrategyBase, "test");
        }
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        public static string ToJson(this object o) => JsonConvert.SerializeObject(o, Formatting.None, JsonSettings);

        public static void LogIn(this SeleniumTester s, string email)
        {
            s.Driver.FindElement(By.Id("Email")).SendKeys(email);
            s.Driver.FindElement(By.Id("Password")).SendKeys("123456");
            s.Driver.FindElement(By.Id("LoginButton")).Click();
            s.Driver.AssertNoError();
        }

        public static void AssertNoError(this IWebDriver driver)
        {
            if (driver.PageSource.Contains("alert-danger"))
            {
                foreach (var dangerAlert in driver.FindElements(By.ClassName("alert-danger")))
                    Assert.False(dangerAlert.Displayed, $"No alert should be displayed, but found this on {driver.Url}: {dangerAlert.Text}");
            }
            Assert.DoesNotContain("errors", driver.Url);
            Assert.DoesNotContain("Error", driver.Title, StringComparison.OrdinalIgnoreCase);
        }

        public static T AssertViewModel<T>(this IActionResult result)
        {
            Assert.NotNull(result);
            var vr = Assert.IsType<ViewResult>(result);
            return Assert.IsType<T>(vr.Model);
        }
        public static async Task<T> AssertViewModelAsync<T>(this Task<IActionResult> task)
        {
            var result = await task;
            Assert.NotNull(result);
            var vr = Assert.IsType<ViewResult>(result);
            return Assert.IsType<T>(vr.Model);
        }

        // Sometimes, selenium is flaky...
        public static IWebElement FindElementUntilNotStaled(this IWebDriver driver, By by, Action<IWebElement> act)
        {
retry:
            try
            {
                var el = driver.FindElement(by);
                act(el);
                return el;
            }
            catch (StaleElementReferenceException)
            {
                goto retry;
            }
        }

        public static void AssertElementNotFound(this IWebDriver driver, By by)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            var wait = SeleniumTester.ImplicitWait;

            while (DateTimeOffset.UtcNow - now < wait)
            {
                try
                {
                    var webElement = driver.FindElement(by);
                    if (!webElement.Displayed)
                        return;
                }
                catch (NoSuchWindowException)
                {
                    return;
                }
                catch (NoSuchElementException)
                {
                    return;
                }
                Thread.Sleep(50);
            }
            Assert.Fail("Elements was found");
        }

        public static void UntilJsIsReady(this WebDriverWait wait)
        {
            wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
            wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return typeof(jQuery) === 'undefined' || jQuery.active === 0").Equals(true));
        }

        // Open collapse via JS, because if we click the link it triggers the toggle animation.
        // This leads to Selenium trying to click the button while it is moving resulting in an error.
        public static void ToggleCollapse(this IWebDriver driver, string collapseId)
        {
            driver.ExecuteJavaScript($"document.getElementById('{collapseId}').classList.add('show')");
        }

        public static void SetAttribute(this IWebDriver driver, string element, string attribute, string value)
        {
            driver.ExecuteJavaScript($"document.getElementById('{element}').setAttribute('{attribute}', '{value}')");
        }
        public static void InvokeJSFunction(this IWebDriver driver, string element, string funcName)
        {
            driver.ExecuteJavaScript($"document.getElementById('{element}').{funcName}()");
        }

        public static void WaitWalletTransactionsLoaded(this IWebDriver driver)
        {
            var wait = new WebDriverWait(driver, SeleniumTester.ImplicitWait);
            wait.UntilJsIsReady();
            wait.Until(d => d.WaitForElement(By.CssSelector("#WalletTransactions[data-loaded='true']")));
        }

        public static IWebElement WaitForElement(this IWebDriver driver, By selector)
        {
            var wait = new WebDriverWait(driver, SeleniumTester.ImplicitWait);
            wait.UntilJsIsReady();

            var el = driver.FindElement(selector);
            wait.Until(d => el.Displayed);

            return el;
        }

        public static void FillIn(this IWebElement el, string text)
        {
            el.Clear();
            el.SendKeys(text);
        }

        public static void ScrollTo(this IWebDriver driver, IWebElement element)
        {
            driver.ExecuteJavaScript("arguments[0].scrollIntoView();", element);
        }

        public static void ScrollTo(this IWebDriver driver, By selector)
        {
            ScrollTo(driver, driver.FindElement(selector));
        }

        public static void WaitUntilAvailable(this IWebDriver driver, By selector, TimeSpan? waitTime = null)
        {
            // Try fast path
            var wait = new WebDriverWait(driver, SeleniumTester.ImplicitWait);
            try
            {
                var el = driver.FindElement(selector);
                wait.Until(_ => el.Displayed && el.Enabled);
                return;
            }
            catch { }

            // Sometimes, selenium complain, so we enter hack territory
            wait.UntilJsIsReady();

            int retriesLeft = 4;
retry:
            try
            {
                var el = driver.FindElement(selector);
                wait.Until(_ => el.Displayed && el.Enabled);
                driver.ScrollTo(selector);
                driver.FindElement(selector);
            }
            catch (NoSuchElementException) when (retriesLeft > 0)
            {
                retriesLeft--;
                if (waitTime != null)
                    Thread.Sleep(waitTime.Value);
                goto retry;
            }
            wait.UntilJsIsReady();
        }

        public static void WaitForAndClick(this IWebDriver driver, By selector)
        {
            driver.WaitUntilAvailable(selector);
            driver.FindElement(selector).Click();
        }

        public static bool ElementDoesNotExist(this IWebDriver driver, By selector)
        {
            Assert.Throws<NoSuchElementException>(
            [DebuggerStepThrough]
            () =>
            {
                driver.FindElement(selector);
            });

            return true;
        }

        public static bool SetCheckbox(this IWebDriver driver, By selector, bool value)
        {
            var element = driver.FindElement(selector);
            if (value != element.Selected)
            {
                driver.WaitForAndClick(selector);
                return true;
            }
            return false;
        }
    }
}
