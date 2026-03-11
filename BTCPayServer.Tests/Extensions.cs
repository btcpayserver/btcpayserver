using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace BTCPayServer.Tests
{
    public static class Extensions
    {
        public static Task<NewTransactionEvent> WaitReceive(this NBXplorer.WebsocketNotificationSession notifications, DerivationStrategyBase target, Func<NewTransactionEvent, bool> predicate = null, CancellationToken cancellationToken = default)
        => WaitNext<NewTransactionEvent>(notifications, e => e.DerivationStrategy == target && (predicate is null || predicate(e)), cancellationToken);
        public static async Task<TEvent> WaitNext<TEvent>(this NBXplorer.WebsocketNotificationSession notifications, Func<TEvent, bool> predicate, CancellationToken cancellationToken = default) where TEvent : NewEventBase
        {
            retry:
            var evt = await notifications.NextEventAsync(cancellationToken);
            if (evt is TEvent { } e)
            {
                if (predicate(e))
                    return e;
            }
            goto retry;
        }
        public static Task<KeyPathInformation> ReserveAddressAsync(this BTCPayWallet wallet, DerivationStrategyBase derivationStrategyBase)
        {
            return wallet.ReserveAddressAsync(null, derivationStrategyBase, "test");
        }
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
        public static string ToJson(this object o) => JsonConvert.SerializeObject(o, Formatting.None, JsonSettings);

        public static string NormalizeWhitespaces(this string input) =>
            string.Concat((input??"").Where(c => !char.IsWhiteSpace(c)));

        public static async Task AssertNoError(this IPage page)
        {
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            var pageSource = await page.ContentAsync();
            if (pageSource.Contains("alert-danger"))
            {
                var dangerAlerts = page.Locator(".alert-danger");
                int count = await dangerAlerts.CountAsync();
                for (int i = 0; i < count; i++)
                {
                    var alert = dangerAlerts.Nth(i);
                    if (await alert.IsVisibleAsync())
                    {
                        var alertText = await alert.InnerTextAsync();
                        Assert.Fail($"No alert should be displayed, but found this on {page.Url}: {alertText}");
                    }
                }
            }
            Assert.DoesNotContain("errors", page.Url);
            var title = await page.TitleAsync();
            Assert.DoesNotContain("Error", title, StringComparison.OrdinalIgnoreCase);
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
