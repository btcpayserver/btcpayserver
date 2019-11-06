using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using OpenQA.Selenium;
using Xunit;

namespace BTCPayServer.Tests
{
    public static class Extensions
    {
        public static void ScrollTo(this IWebDriver driver, By by)
        {
            var element = driver.FindElement(by);
        }
        /// <summary>
        /// Sometimes the chrome driver is fucked up and we need some magic to click on the element.
        /// </summary>
        /// <param name="element"></param>
        public static void ForceClick(this IWebElement element)
        {
            element.SendKeys(Keys.Return);
        }
        public static void AssertNoError(this IWebDriver driver)
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
                foreach (var logKind in new []{ LogType.Browser, LogType.Client, LogType.Driver, LogType.Server })
                {
                    try
                    {
                        var logs = driver.Manage().Logs.GetLog(logKind);
                        builder.AppendLine($"Selenium [{logKind}]:");
                        foreach (var entry in logs)
                        {
                            builder.AppendLine($"[{entry.Level}]: {entry.Message}");
                        }
                    }
                    catch { }
                    builder.AppendLine($"---------");
                }
                Logs.Tester.LogInformation(builder.ToString());
                builder = new StringBuilder();
                builder.AppendLine($"Selenium [Sources]:");
                builder.AppendLine(driver.PageSource);
                builder.AppendLine($"---------");
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
                catch (NoSuchElementException)
                {
                    return;
                }
                Thread.Sleep(50);
            }
            Assert.False(true, "Elements was found");
        }
    }
}
