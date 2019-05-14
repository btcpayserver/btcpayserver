using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OpenQA.Selenium;
using Xunit;

namespace BTCPayServer.Tests
{
    public static class Extensions
    {
        public static void AssertNoError(this IWebDriver driver)
        {
            try
            {
                Assert.NotNull(driver.FindElement(By.ClassName("navbar-brand")));
            }
            catch
            {
                StringBuilder builder = new StringBuilder();
                foreach (var logKind in new []{ LogType.Browser, LogType.Client, LogType.Driver })
                {
                    builder.AppendLine($"Selenium [{logKind}]:");
                    foreach (var entry in driver.Manage().Logs.GetLog(logKind))
                    {
                        builder.AppendLine($"[{entry.Level}]: {entry.Message}");
                    }
                    builder.AppendLine($"---------");
                }
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
    }
}
