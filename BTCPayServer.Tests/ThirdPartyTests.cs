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
using BTCPayServer.HostedServices;
using BTCPayServer.Hosting;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Rating;
using BTCPayServer.Services.Fees;
using BTCPayServer.Services.Rates;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileSystemGlobbing;
using NBitcoin;
using NBitpayClient;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using static BTCPayServer.HostedServices.PullPaymentHostedService.PayoutApproval;

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

            var fileId = await UnitTest1.CanUploadFile(controller);
            await UnitTest1.CanRemoveFile(controller, fileId);
        }

        [Fact(Skip = "Fail on CI")]
        public async Task CanQueryMempoolFeeProvider()
        {
            IServiceCollection collection = new ServiceCollection();
            collection.AddMemoryCache();
            collection.AddHttpClient();
            var prov = collection.BuildServiceProvider();
            foreach (var isTestnet in new[] { true, false })
            {
                var mempoolSpaceFeeProvider = new MempoolSpaceFeeProvider(
                    prov.GetService<IMemoryCache>(),
                    "test" + isTestnet,
                    prov.GetService<IHttpClientFactory>(),
                    isTestnet);
                mempoolSpaceFeeProvider.CachedOnly = true;
                await Assert.ThrowsAsync<InvalidOperationException>(() => mempoolSpaceFeeProvider.GetFeeRateAsync());
                mempoolSpaceFeeProvider.CachedOnly = false;
                var rates = await mempoolSpaceFeeProvider.GetFeeRatesAsync();
                mempoolSpaceFeeProvider.CachedOnly = true;
                await mempoolSpaceFeeProvider.GetFeeRateAsync();
                mempoolSpaceFeeProvider.CachedOnly = false;
                Assert.NotEmpty(rates);


                var recommendedFees =
                    await Task.WhenAll(new[]
                        {
                            TimeSpan.FromMinutes(10.0), TimeSpan.FromMinutes(60.0), TimeSpan.FromHours(6.0),
                            TimeSpan.FromHours(24.0),
                        }.Select(async time =>
                        {
                            try
                            {
                                var result = await mempoolSpaceFeeProvider.GetFeeRateAsync(
                                    (int)Network.Main.Consensus.GetExpectedBlocksFor(time));
                                return new WalletSendModel.FeeRateOption()
                                {
                                    Target = time,
                                    FeeRate = result.SatoshiPerByte
                                };
                            }
                            catch (Exception)
                            {
                                return null;
                            }
                        })
                        .ToArray());
                //ENSURE THESE ARE LOGICAL
                Assert.True(recommendedFees[0].FeeRate >= recommendedFees[1].FeeRate, $"{recommendedFees[0].Target}:{recommendedFees[0].FeeRate} >= {recommendedFees[1].Target}:{recommendedFees[1].FeeRate}");
                Assert.True(recommendedFees[1].FeeRate >= recommendedFees[2].FeeRate, $"{recommendedFees[1].Target}:{recommendedFees[1].FeeRate} >= {recommendedFees[2].Target}:{recommendedFees[2].FeeRate}");
                Assert.True(recommendedFees[2].FeeRate >= recommendedFees[3].FeeRate, $"{recommendedFees[2].Target}:{recommendedFees[2].FeeRate} >= {recommendedFees[3].Target}:{recommendedFees[3].FeeRate}");
            }
        }
        [Fact]
        public async Task CanQueryDirectProviders()
        {
            // TODO: Check once in a while whether or not they are working again
            string[] brokenShitcoinCasinos = { "bitnob", "binance", "coinbasepro" };
            var skipped = 0;
            var factory = FastTests.CreateBTCPayRateFactory();
            var directlySupported = factory.AvailableRateProviders.Where(s => s.Source == RateSource.Direct)
                .Select(s => s.Id).ToHashSet();
            var providerList = factory
                .Providers
                .Where(p => p.Value is BackgroundFetcherRateProvider bf &&
                            !(bf.Inner is CoinGeckoRateProvider cg && cg.UnderlyingExchange != null))
                .Select(p => (ExpectedName: p.Key, ResultAsync: p.Value.GetRatesAsync(default),
                    Fetcher: (BackgroundFetcherRateProvider)p.Value))
                .ToList();
            foreach (var result in providerList)
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
                else if (name == "bitnob")
                {
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "NGN") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 NGN
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
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "XPT") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 LBP (I hope)
                }
                else if (name == "bitmynt")
                {
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "NOK") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 NOK
                }
                else if (name == "barebitcoin")
                {
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "NOK") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 NOK
                }
                else if (name == "coinmate")
                {
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "EUR") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 EUR
                    Assert.Contains(exchangeRates.ByExchange[name],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "CZK") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 CZK
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
            await factory.Providers["kraken"].GetRatesAsync(default);

            var p = new KrakenExchangeRateProvider();
            var rates = await p.GetRatesAsync(default);
            Assert.Contains(rates, e => e.CurrencyPair == new CurrencyPair("XMR", "BTC") && e.BidAsk.Bid < 1.0m);

            // Check we didn't skip too many exchanges
            Assert.InRange(skipped, 0, 5);
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
                "https://www.coingecko.com", // unhappy service
                "https://www.wasabiwallet.io", // Banning US, CI unhappy
                "https://fullynoded.app", // Sometimes DNS doesn't work
                "https://hrf.org" // Started returning Forbidden
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
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    TestLogs.LogInformation($"TooManyRequests, skipping: {url} ({file})");
                }
                else
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    if (uri.Fragment.Length != 0)
                    {
                        var fragment = uri.Fragment.Substring(1);
                        var contents = await response.Content.ReadAsStringAsync();
                        Assert.Matches($"id=\"{fragment}\"", contents);
                    }

                    TestLogs.LogInformation($"OK: {url} ({file})");
                }
            }
            catch (Exception ex) when (ex is MatchesException)
            {
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
                var details = ex.Message;
                TestLogs.LogInformation($"FAILED: {url} ({file}) {details}");

                throw;
            }
        }

        [Fact]
        public async Task CanSolveTheDogesRatesOnKraken()
        {
            var factory = FastTests.CreateBTCPayRateFactory();
            var fetcher = new RateFetcher(factory);

            Assert.True(RateRules.TryParse("X_X=kraken(X_BTC) * kraken(BTC_X)", out var rule));
            foreach (var pair in new[] { "DOGE_USD", "DOGE_CAD" })
            {
                var result = await fetcher.FetchRate(CurrencyPair.Parse(pair), rule, null, default);
                Assert.NotNull(result.BidAsk);
                Assert.Empty(result.Errors);
            }
        }

        [Fact]
        public async Task CanGetRateFromRecommendedExchanges()
        {
            var factory = FastTests.CreateBTCPayRateFactory();
            var fetcher = new RateFetcher(factory);
            var provider = CreateDefaultRates(ChainName.Mainnet);
            var defaultRules = new DefaultRulesCollection(provider.Select(p => p.DefaultRates));
            var b = new StoreBlob();
            string[] temporarilyBroken = Array.Empty<string>();
            foreach (var k in defaultRules.RecommendedExchanges)
            {
                b.DefaultCurrency = k.Key;
                var rules = b.GetOrCreateRateSettings(false).GetDefaultRateRules(defaultRules, b.Spread);
                var pairs = new[] { CurrencyPair.Parse($"BTC_{k.Key}") }.ToHashSet();
                var result = fetcher.FetchRates(pairs, rules, null, default);
                foreach ((CurrencyPair key, Task<RateResult> value) in result)
                {
                    TestLogs.LogInformation($"Testing {key} when default currency is {k.Key}");
                    var rateResult = await value;
                    var hasRate = rateResult.BidAsk != null;

                    if (temporarilyBroken.Contains(k.Key))
                    {
                        if (!hasRate)
                        {
                            TestLogs.LogInformation($"Skipping {key} because it is marked as temporarily broken");
                            continue;
                        }
                        TestLogs.LogInformation($"Note: {key} is marked as temporarily broken, but the rate is available");
                    }
                    Assert.True(hasRate, $"Impossible to get the rate {rateResult.EvaluatedRule}");
                }
            }
        }

        [Fact]
        public async Task CanGetRateCryptoCurrenciesByDefault()
        {
            using var cts = new CancellationTokenSource(60_000);
            var provider = CreateDefaultRates(ChainName.Mainnet, exchangeRecommendation: true);
            var defaultRules = new DefaultRulesCollection(provider.Select(p => p.DefaultRates));
            var factory = FastTests.CreateBTCPayRateFactory();
            var fetcher = new RateFetcher(factory);
            var pairs =
                provider
                    .Where(c => c.CryptoCode is not null)
                    .Select(c => new CurrencyPair(c.CryptoCode, "USD"))
                    .ToHashSet();

            string[] brokenShitcoins = { "BTG", "BTX", "GRS" };
            bool IsBrokenShitcoin(CurrencyPair p) => brokenShitcoins.Contains(p.Left) || brokenShitcoins.Contains(p.Right);
            foreach (var _ in brokenShitcoins)
            {
                foreach (var p in pairs.Where(IsBrokenShitcoin).ToArray())
                {
                    TestLogs.LogInformation($"Skipping {p} because it is marked as broken");
                    pairs.Remove(p);
                }
            }

            var rules = new StoreBlob().GetOrCreateRateSettings(false).GetDefaultRateRules(defaultRules, 0.0m);
            var result = fetcher.FetchRates(pairs, rules, null, cts.Token);
            foreach ((CurrencyPair key, Task<RateResult> value) in result)
            {
                var rateResult = await value;
                TestLogs.LogInformation($"Testing {key}");
                Assert.True(rateResult.BidAsk != null, $"Impossible to get the rate {rateResult.EvaluatedRule}");
            }
        }

        private IEnumerable<(string CryptoCode, DefaultRules DefaultRates)> CreateDefaultRates(ChainName chainName, bool exchangeRecommendation = false)
        {
            var results = new List<(string CryptoCode, DefaultRules DefaultRates)>();
            var prov = CreateNetworkProvider(chainName);
            foreach (var network in prov.GetAll())
            {
                results.Add((network.CryptoCode, new DefaultRules(network.DefaultRateRules)));
            }
            if (exchangeRecommendation)
            {
                ServiceCollection services = new ServiceCollection();
                BTCPayServerServices.RegisterExchangeRecommendations(services);
                foreach (var rule in services.BuildServiceProvider().GetRequiredService<IEnumerable<DefaultRules>>())
                {
                    results.Add((null, rule));
                }
            }
            return results;
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CheckJsContent()
        {
            var handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli;
            // This test verify that no malicious js is added in the minified files.
            // We should extend the tests to other js files, but we can do as we go...
            using var client = new HttpClient(handler);
            var actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "bootstrap", "bootstrap.bundle.min.js").Trim();
            var version = Regex.Match(actual, "Bootstrap v([0-9]+.[0-9]+.[0-9]+)").Groups[1].Value;
            var expected = (await (await client.GetAsync($"https://cdn.jsdelivr.net/npm/bootstrap@{version}/dist/js/bootstrap.bundle.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "clipboard.js", "clipboard.js");
            expected = (await (await client.GetAsync("https://cdnjs.cloudflare.com/ajax/libs/clipboard.js/2.0.8/clipboard.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "vuejs", "vue.min.js").Trim();
            version = Regex.Match(actual, "Vue\\.js v([0-9]+.[0-9]+.[0-9]+)").Groups[1].Value;
            expected = (await (await client.GetAsync($"https://cdnjs.cloudflare.com/ajax/libs/vue/{version}/vue.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "i18next", "i18next.min.js").Trim();
            expected = (await (await client.GetAsync("https://cdnjs.cloudflare.com/ajax/libs/i18next/22.0.6/i18next.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "i18next", "i18nextHttpBackend.min.js").Trim();
            expected = (await (await client.GetAsync("https://cdnjs.cloudflare.com/ajax/libs/i18next-http-backend/2.0.1/i18nextHttpBackend.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "i18next", "vue-i18next.js").Trim();
            expected = (await (await client.GetAsync("https://unpkg.com/@panter/vue-i18next@0.15.2/dist/vue-i18next.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "vue-qrcode", "vue-qrcode.min.js").Trim();
            version = Regex.Match(actual, "vue-qrcode v([0-9]+.[0-9]+.[0-9]+)").Groups[1].Value;
            expected = (await (await client.GetAsync($"https://unpkg.com/@chenfengyuan/vue-qrcode@{version}/dist/vue-qrcode.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "tom-select", "tom-select.complete.min.js").Trim();
            version = Regex.Match(actual, "Tom Select v([0-9]+.[0-9]+.[0-9]+)").Groups[1].Value;
            expected = (await (await client.GetAsync($"https://cdn.jsdelivr.net/npm/tom-select@{version}/dist/js/tom-select.complete.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            // This test is flaky probably because of the CDN sending the wrong file's version in some regions.
            // https://app.circleci.com/pipelines/github/btcpayserver/btcpayserver/13750/workflows/44aaf31d-0057-4fd8-a5bb-1a2c47fc530f/jobs/42963
            // It works locally depending on where you live.

            //actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "dom-confetti", "dom-confetti.min.js").Trim();
            //version = Regex.Match(actual, "Original file: /npm/dom-confetti@([0-9]+.[0-9]+.[0-9]+)/lib/main.js").Groups[1].Value;
            //expected = (await (await client.GetAsync($"https://cdn.jsdelivr.net/npm/dom-confetti@{version}")).Content.ReadAsStringAsync()).Trim();
            //EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "vue-sortable", "sortable.min.js").Trim();
            version = Regex.Match(actual, "Sortable ([0-9]+.[0-9]+.[0-9]+) ").Groups[1].Value;
            expected = (await (await client.GetAsync($"https://unpkg.com/sortablejs@{version}/Sortable.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "bootstrap-vue", "bootstrap-vue.min.js").Trim();
            version = Regex.Match(actual, "BootstrapVue ([0-9]+.[0-9]+.[0-9]+)").Groups[1].Value;
            expected = (await (await client.GetAsync($"https://cdnjs.cloudflare.com/ajax/libs/bootstrap-vue/{version}/bootstrap-vue.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "FileSaver", "FileSaver.min.js").Trim();
            expected = (await (await client.GetAsync($"https://raw.githubusercontent.com/eligrey/FileSaver.js/43bbd2f0ae6794f8d452cd360e9d33aef6071234/dist/FileSaver.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "papaparse", "papaparse.min.js").Trim();
            expected = (await (await client.GetAsync($"https://raw.githubusercontent.com/mholt/PapaParse/5.4.1/papaparse.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "vue-sanitize-directive", "vue-sanitize-directive.umd.min.js").Trim();
            version = Regex.Match(actual, "Original file: /npm/vue-sanitize-directive@([0-9]+.[0-9]+.[0-9]+)").Groups[1].Value;
            expected = (await (await client.GetAsync($"https://cdn.jsdelivr.net/npm/vue-sanitize-directive@{version}/dist/vue-sanitize-directive.umd.min.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);

            // Somehow, cdn.jsdelivr.net always change the minifier breaking this test time to time...
            // actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "decimal.js", "decimal.min.js").Trim();
            // version = Regex.Match(actual, "Original file: /npm/decimal\\.js@([0-9]+.[0-9]+.[0-9]+)/decimal\\.js").Groups[1].Value;
            // expected = (await (await client.GetAsync($"https://cdn.jsdelivr.net/npm/decimal.js@{version}/decimal.min.js")).Content.ReadAsStringAsync()).Trim();
            // EqualJsContent(expected, actual);

            actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "bbqr", "bbqr.iife.js").Trim();
            expected = (await (await client.GetAsync($"https://cdn.jsdelivr.net/npm/bbqr@1.0.0/dist/bbqr.iife.js")).Content.ReadAsStringAsync()).Trim();
            EqualJsContent(expected, actual);
        }

        private void EqualJsContent(string expected, string actual)
        {
            if (expected != actual)
                 Assert.Equal(expected, actual.ReplaceLineEndings("\n"));
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
                Assert.Single(rates, r => r == rate);
            }
        }

        private static async Task<decimal> CreateInvoice(ServerTester tester, TestAccount user, string exchange,
            string currency = "USD")
        {
            var storeController = user.GetController<UIStoresController>();
            var vm = (RatesViewModel)((ViewResult)await storeController.Rates()).Model;
            vm.PrimarySource.PreferredExchange = exchange;
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
