using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using OpenQA.Selenium;
using PlaywrightSharp;
using Xunit;

namespace BTCPayServer.Tests
{
    public static class PlayWrightExtensions
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
                Assert.NotEmpty(await driver.QuerySelectorAllAsync("navbar-brand"));
                
                if ((await driver.GetContentAsync()).Contains("alert-danger"))
                {
                    foreach (var dangerAlert in await driver.QuerySelectorAllAsync("alert-danger"))
                        Assert.False(await dangerAlert.IsVisibleAsync(), "No alert should be displayed");
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

        public static async Task AssertElementNotFound(this IPage driver, string selector)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            var wait = PlayWrightTester.ImplicitWait;

            while (DateTimeOffset.UtcNow - now < wait)
            {
                try
                {
                    var webElement = await driver.QuerySelectorAsync(selector);
                    if (!await webElement.IsVisibleAsync())
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
