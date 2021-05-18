using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using PlaywrightSharp;
using Xunit;

namespace BTCPayServer.Tests
{
    public static class PlayerwightExtensions
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        public static string ToJson(this object o) => JsonConvert.SerializeObject(o, Formatting.None, JsonSettings);

        public static async Task LogIn(this PlayWrightTester s, string email)
        {
            await s.Page.TypeAsync("#Email", email);
            await s.Page.TypeAsync("#Password", "123456");
            await s.Page.ClickAsync("#LoginButton");
            await s.Page.AssertNoError();
        }

        public static async Task AssertNoError(this IPage driver)
        {
            try
            {
                Assert.NotEmpty(driver.FindElements(By.ClassName("navbar-brand")));
                if (driver.PageSource.Contains("alert-danger"))
                {
                    foreach (var dangerAlert in driver.FindElements(By.ClassName("alert-danger")))
                        Assert.False(dangerAlert.Displayed, "No alert should be displayed");
                }
            }
            catch
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine();
                foreach (var logKind in new[] { LogType.Browser, LogType.Client, LogType.Driver, LogType.Server })
                {
                    try
                    {
                        var logs = driver.Manage().Logs.GetLog(logKind);
                        builder.AppendLine($"PlayWright [{logKind}]:");
                        foreach (var entry in logs)
                        {
                            builder.AppendLine($"[{entry.Level}]: {entry.Message}");
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    builder.AppendLine("---------");
                }
                Logs.Tester.LogInformation(builder.ToString());
                builder = new StringBuilder();
                builder.AppendLine("PlayWright [Sources]:");
                builder.AppendLine(await driver.GetContentAsync());
                builder.AppendLine("---------");
                Logs.Tester.LogInformation(builder.ToString());
                throw;
            }
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

        public static void AssertElementNotFound(this IPage driver, By by)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            var wait = PlayWrightTester.ImplicitWait;

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
            Assert.False(true, "Elements was found");
        }

        public static void UntilJsIsReady(this WebDriverWait wait)
        {
            wait.Until(d=>((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
            wait.Until(d=>((IJavaScriptExecutor)d).ExecuteScript("return typeof(jQuery) === 'undefined' || jQuery.active === 0").Equals(true));
        }

        public static IWebElement WaitForElement(this IPage driver, By selector)
        {
            var wait = new WebDriverWait(driver, PlayWrightTester.ImplicitWait);
            wait.UntilJsIsReady();

            var el = driver.FindElement(selector);
            wait.Until(d => el.Displayed);

            return el;
        }

        public static void WaitForAndClick(this IPage driver, By selector)
        {
            var wait = new WebDriverWait(driver, PlayWrightTester.ImplicitWait);
            wait.UntilJsIsReady();

            var el = driver.FindElement(selector);
            wait.Until(d => el.Displayed && el.Enabled);
            el.Click();

            wait.UntilJsIsReady();
        }

        public static async Task SetCheckbox(this IPage driver, string selector, bool value)
        {
            var element = await driver.QuerySelectorAsync(selector);
            if (value)
            {
                await element.CheckAsync();
            }
            else
            {
                await element.UncheckAsync();
            }
        }
    }
}
