using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NBitcoin;
using NBitpayClient;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace BTCPayServer.Tests
{
    [Trait("ThirdParty", "ThirdParty")]
    public class ThirdPartyTests : UnitTestBase
    {

        public ThirdPartyTests(ITestOutputHelper helper) : base(helper)
        {

        }

        [FactWithSecret("AzureBlobStorageConnectionString")]
        public async Task CanUseAzureBlobStorage()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();
            var controller = tester.PayTester.GetController<UIServerController>(user.UserId, user.StoreId);
            var azureBlobStorageConfiguration = Assert.IsType<AzureBlobStorageConfiguration>(Assert
                .IsType<ViewResult>(await controller.StorageProvider(StorageProvider.AzureBlobStorage.ToString()))
                .Model);

            azureBlobStorageConfiguration.ConnectionString = FactWithSecretAttribute.GetFromSecrets("AzureBlobStorageConnectionString");
            azureBlobStorageConfiguration.ContainerName = "testscontainer";
            Assert.IsType<ViewResult>(
                await controller.EditAzureBlobStorageStorageProvider(azureBlobStorageConfiguration));


            var shouldBeRedirectingToAzureStorageConfigPage =
                Assert.IsType<RedirectToActionResult>(await controller.Storage());
            Assert.Equal(nameof(StorageProvider), shouldBeRedirectingToAzureStorageConfigPage.ActionName);
            Assert.Equal(StorageProvider.AzureBlobStorage,
                shouldBeRedirectingToAzureStorageConfigPage.RouteValues["provider"]);

            //seems like azure config worked, let's see if the conn string was actually saved

            Assert.Equal(azureBlobStorageConfiguration.ConnectionString, Assert
                .IsType<AzureBlobStorageConfiguration>(Assert
                    .IsType<ViewResult>(
                        await controller.StorageProvider(StorageProvider.AzureBlobStorage.ToString()))
                    .Model).ConnectionString);



            await UnitTest1.CanUploadRemoveFiles(controller);
        }

        [Fact]
        public async Task CanQueryDirectProviders()
        {
            // TODO: Check once in a while whether or not they are working again
            string[] brokenShitcoinCasinos = {};
            var skipped = 0;
            var factory = FastTests.CreateBTCPayRateFactory();
            var directlySupported = factory.GetSupportedExchanges().Where(s => s.Source == RateSource.Direct)
                .Select(s => s.Id).ToHashSet();
            foreach (var result in factory
                .Providers
                .Where(p => p.Value is BackgroundFetcherRateProvider bf &&
                            !(bf.Inner is CoinGeckoRateProvider cg && cg.UnderlyingExchange != null))
                .Select(p => (ExpectedName: p.Key, ResultAsync: p.Value.GetRatesAsync(default),
                    Fetcher: (BackgroundFetcherRateProvider)p.Value))
                .ToList())
            {
                var name = result.ExpectedName;
                if (brokenShitcoinCasinos.Contains(name))
                {
                    TestLogs.LogInformation($"Skipping {name}: Broken shitcoin casino");
                    skipped++;
                    continue;
                }
                
                TestLogs.LogInformation($"Testing {name}");

                result.Fetcher.InvalidateCache();

                ExchangeRates exchangeRates = null;
                try
                {
                    exchangeRates = new ExchangeRates(name, result.ResultAsync.Result);
                }
                catch (Exception exception)
                {
                    TestLogs.LogInformation($"Skipping {name}: {exception.Message}");
                    skipped++;
                    continue;
                }
                result.Fetcher.InvalidateCache();
                Assert.NotNull(exchangeRates);
                Assert.NotEmpty(exchangeRates);
                Assert.NotEmpty(exchangeRates.ByExchange[name]);
                if (name == "bitbank" || name == "bitflyer")
                {
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "JPY") &&
                             e.BidAsk.Bid > 100m); // 1BTC will always be more than 100JPY
                }
                else if (name == "argoneum")
                {
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "AGM") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 AGM
                }
                else if (name == "ripio")
                {
                    // Ripio keeps changing their pair, so anything is fine...
                    Assert.NotEmpty(exchangeRates.ByExchange[name]);
                }
                else if (name == "cryptomarket")
                {
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "CLP") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 CLP
                }
                else if (name == "yadio")
                {
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "LBP") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 LBP (I hope)
                }
                else
                {
                    if (name == "kraken")
                    {
                        Assert.Contains(exchangeRates.ByExchange[name], e => e.CurrencyPair == new CurrencyPair("XMR", "BTC") && e.BidAsk.Bid < 1.0m);
                    }
                    // This check if the currency pair is using right currency pair
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => (e.CurrencyPair == new CurrencyPair("BTC", "USD") ||
                                e.CurrencyPair == new CurrencyPair("BTC", "EUR") ||
                                e.CurrencyPair == new CurrencyPair("BTC", "USDT") ||
                                e.CurrencyPair == new CurrencyPair("BTC", "USDC") ||
                                e.CurrencyPair == new CurrencyPair("BTC", "CAD") ||
                                e.CurrencyPair == new CurrencyPair("BTC", "CLP"))
                                && e.BidAsk.Bid > 1.0m // 1BTC will always be more than 1USD
                    );
                }
                // We are not showing a directly implemented exchange as directly implemented in the UI
                // we need to modify the AvailableRateProvider

                // There are some exception we stopped supporting but don't want to break backward compat
                if (name != "coinaverage" && name != "gdax")
                    Assert.Contains(name, directlySupported);
            }

            // Kraken emit one request only after first GetRates
            factory.Providers["kraken"].GetRatesAsync(default).GetAwaiter().GetResult();

            var p = new KrakenExchangeRateProvider();
            var rates = await p.GetRatesAsync(default);
            Assert.Contains(rates, e => e.CurrencyPair == new CurrencyPair("XMR", "BTC") && e.BidAsk.Bid < 1.0m);
            
            // Check we didn't skip too many exchanges
            Assert.InRange(skipped, 0, 3);
        }

        [Fact]
        public async Task CheckNoDeadLink()
        {
            var views = Path.Combine(TestUtils.TryGetSolutionDirectoryInfo().FullName, "BTCPayServer", "Views");
            var viewFiles = Directory.EnumerateFiles(views, "*.cshtml", SearchOption.AllDirectories).ToArray();
            Assert.NotEmpty(viewFiles);
            Regex regex = new Regex("href=\"(http.*?)\"");
            var httpClient = new HttpClient();
            List<Task> checkLinks = new List<Task>();
            foreach (var file in viewFiles)
            {
                checkLinks.Add(CheckDeadLinks(regex, httpClient, file));
            }

            await Task.WhenAll(checkLinks);
        }

        private async Task CheckDeadLinks(Regex regex, HttpClient httpClient, string file)
        {
            List<Task> checkLinks = new List<Task>();
            var text = await File.ReadAllTextAsync(file);

            var urlBlacklist = new string[]
            {
                "https://www.btse.com", // not allowing to be hit from circleci
                "https://www.bitpay.com", // not allowing to be hit from circleci
                "https://support.bitpay.com",
                "https://www.pnxbet.com" //has geo blocking
            };

            foreach (var match in regex.Matches(text).OfType<Match>())
            {
                var url = match.Groups[1].Value;
                if (urlBlacklist.Any(a => url.StartsWith(a.ToLowerInvariant())))
                    continue;
                checkLinks.Add(AssertLinkNotDead(httpClient, url, file));
            }

            await Task.WhenAll(checkLinks);
        }

        private async Task AssertLinkNotDead(HttpClient httpClient, string url, string file)
        {
            var uri = new Uri(url);
            int retryLeft = 3;
            retry:
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                request.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:75.0) Gecko/20100101 Firefox/75.0");
                using var cts = new CancellationTokenSource(5_000);
                var response = await httpClient.SendAsync(request, cts.Token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                if (uri.Fragment.Length != 0)
                {
                    var fragment = uri.Fragment.Substring(1);
                    var contents = await response.Content.ReadAsStringAsync();
                    Assert.Matches($"id=\"{fragment}\"", contents);
                }

                TestLogs.LogInformation($"OK: {url} ({file})");
            }
            catch (Exception ex) when (ex is MatchesException)
            {
                var details = ex.Message;
                TestLogs.LogInformation($"FAILED: {url} ({file}) – anchor not found: {uri.Fragment}");

                throw;
            }
            catch (Exception) when (retryLeft > 0)
            {
                retryLeft--;
                goto retry;
            }
            catch (Exception ex)
            {
                var details = ex is EqualException ? (ex as EqualException).Actual : ex.Message;
                TestLogs.LogInformation($"FAILED: {url} ({file}) {details}");

                throw;
            }
        }

        [Fact()]
        public void CanSolveTheDogesRatesOnKraken()
        {
            var provider = new BTCPayNetworkProvider(ChainName.Mainnet);
            var factory = FastTests.CreateBTCPayRateFactory();
            var fetcher = new RateFetcher(factory);

            Assert.True(RateRules.TryParse("X_X=kraken(X_BTC) * kraken(BTC_X)", out var rule));
            foreach (var pair in new[] { "DOGE_USD", "DOGE_CAD", "DASH_CAD", "DASH_USD", "DASH_EUR" })
            {
                var result = fetcher.FetchRate(CurrencyPair.Parse(pair), rule, default).GetAwaiter().GetResult();
                Assert.NotNull(result.BidAsk);
                Assert.Empty(result.Errors);
            }
        }

        [Fact]
        public void CanGetRateCryptoCurrenciesByDefault()
        {
            string[] brokenShitcoins = { "BTX_USD", "CHC_USD" };
            var provider = new BTCPayNetworkProvider(ChainName.Mainnet);
            var factory = FastTests.CreateBTCPayRateFactory();
            var fetcher = new RateFetcher(factory);
            var pairs =
                provider.GetAll()
                    .Select(c => new CurrencyPair(c.CryptoCode, "USD"))
                    .ToHashSet();

            var rules = new StoreBlob().GetDefaultRateRules(provider);
            var result = fetcher.FetchRates(pairs, rules, default);
            foreach ((CurrencyPair key, Task<RateResult> value) in result)
            {
                var rateResult = value.GetAwaiter().GetResult();
                TestLogs.LogInformation($"Testing {key}");
                if (brokenShitcoins.Contains(key.ToString()))
                    continue;
                Assert.True(rateResult.BidAsk != null, $"Impossible to get the rate {rateResult.EvaluatedRule}");
            }
        }

        [Fact]
        public async Task CheckJsContent()
        {
            // This test verify that no malicious js is added in the minified files.
            // We should extend the tests to other js files, but we can do as we go...
            using var client = new HttpClient();
            var actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "bootstrap", "bootstrap.bundle.min.js").Trim();
            var version = Regex.Match(actual, "Bootstrap v([0-9]+.[0-9]+.[0-9]+)").Groups[1].Value;
            var expected = (await (await client.GetAsync($"https://cdn.jsdelivr.net/npm/bootstrap@{version}/dist/js/bootstrap.bundle.min.js")).Content.ReadAsStringAsync()).Trim();
            Assert.Equal(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "clipboard.js", "clipboard.js");
            expected = (await (await client.GetAsync("https://cdnjs.cloudflare.com/ajax/libs/clipboard.js/2.0.8/clipboard.js")).Content.ReadAsStringAsync()).Trim();
            Assert.Equal(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "vuejs", "vue.min.js").Trim();
            version = Regex.Match(actual, "Vue\\.js v([0-9]+.[0-9]+.[0-9]+)").Groups[1].Value;
            expected = (await (await client.GetAsync($"https://cdnjs.cloudflare.com/ajax/libs/vue/{version}/vue.min.js")).Content.ReadAsStringAsync()).Trim();
            Assert.Equal(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "i18next", "i18next.min.js").Trim();
            expected = (await (await client.GetAsync("https://cdnjs.cloudflare.com/ajax/libs/i18next/22.0.6/i18next.min.js")).Content.ReadAsStringAsync()).Trim();
            Assert.Equal(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "i18next", "i18nextHttpBackend.min.js").Trim();
            expected = (await (await client.GetAsync("https://cdnjs.cloudflare.com/ajax/libs/i18next-http-backend/2.0.1/i18nextHttpBackend.min.js")).Content.ReadAsStringAsync()).Trim();
            Assert.Equal(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "i18next", "vue-i18next.js").Trim();
            expected = (await (await client.GetAsync("https://unpkg.com/@panter/vue-i18next@0.15.2/dist/vue-i18next.js")).Content.ReadAsStringAsync()).Trim();
            Assert.Equal(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "vue-qrcode", "vue-qrcode.min.js").Trim();
            version = Regex.Match(actual, "vue-qrcode v([0-9]+.[0-9]+.[0-9]+)").Groups[1].Value;
            expected = (await (await client.GetAsync($"https://unpkg.com/@chenfengyuan/vue-qrcode@{version}/dist/vue-qrcode.min.js")).Content.ReadAsStringAsync()).Trim();
            Assert.Equal(expected, actual);
        }
        
        string GetFileContent(params string[] path)
        {
            var l = path.ToList();
            l.Insert(0, TestUtils.TryGetSolutionDirectoryInfo().FullName);
            return File.ReadAllText(Path.Combine(l.ToArray()));
        }

        [Fact]
        public async Task CanExportBackgroundFetcherState()
        {
            var factory = FastTests.CreateBTCPayRateFactory();
            var provider = (BackgroundFetcherRateProvider)factory.Providers["kraken"];
            await provider.GetRatesAsync(default);
            var state = provider.GetState();
            Assert.Single(state.Rates, r => r.Pair == new CurrencyPair("BTC", "EUR"));
            var provider2 = new BackgroundFetcherRateProvider(provider.Inner)
            {
                RefreshRate = provider.RefreshRate,
                ValidatyTime = provider.ValidatyTime
            };
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                // Should throw
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await provider2.GetRatesAsync(cts.Token));
            }

            provider2.LoadState(state);
            Assert.Equal(provider.LastRequested, provider2.LastRequested);
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                // Should not throw, as things should be cached
                await provider2.GetRatesAsync(cts.Token);
            }

            Assert.Equal(provider.NextUpdate, provider2.NextUpdate);
            Assert.NotEqual(provider.LastRequested, provider2.LastRequested);
            Assert.Equal(provider.Expiration, provider2.Expiration);

            var str = JsonConvert.SerializeObject(state);
            var state2 = JsonConvert.DeserializeObject<BackgroundFetcherState>(str);
            var str2 = JsonConvert.SerializeObject(state2);
            Assert.Equal(str, str2);
        }

        [Fact]
        public async Task CanUseExchangeSpecificRate()
        {
            using var tester = CreateServerTester();
            tester.PayTester.MockRates = false;
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            List<decimal> rates = new List<decimal>();
            rates.Add(await CreateInvoice(tester, user, "coingecko"));
            var bitflyer = await CreateInvoice(tester, user, "bitflyer", "JPY");
            var bitflyer2 = await CreateInvoice(tester, user, "bitflyer", "JPY");
            Assert.Equal(bitflyer, bitflyer2); // Should be equal because cache
            rates.Add(bitflyer);

            foreach (var rate in rates)
            {
                Assert.Single(rates.Where(r => r == rate));
            }
        }

        private static async Task<decimal> CreateInvoice(ServerTester tester, TestAccount user, string exchange,
            string currency = "USD")
        {
            var storeController = user.GetController<UIStoresController>();
            var vm = (RatesViewModel)((ViewResult)storeController.Rates()).Model;
            vm.PreferredExchange = exchange;
            await storeController.Rates(vm);
            var invoice2 = await user.BitPay.CreateInvoiceAsync(
                new Invoice()
                {
                    Price = 5000.0m,
                    Currency = currency,
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);
            return invoice2.CryptoInfo[0].Rate;
        }
    }
}
