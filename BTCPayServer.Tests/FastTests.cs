using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Rates;
using BTCPayServer.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace BTCPayServer.Tests
{
    public class FastTests : UnitTestBase
    {
        public FastTests(ITestOutputHelper helper) : base(helper)
        {

        }
        class DockerImage
        {
            public string User { get; private set; }
            public string Name { get; private set; }
            public string Tag { get; private set; }

            public string Source { get; set; }

            public static DockerImage Parse(string str)
            {
                //${BTCPAY_IMAGE: -btcpayserver / btcpayserver:1.0.3.21}
                var variableMatch = Regex.Match(str, @"\$\{[^-]+-([^\}]+)\}");
                if (variableMatch.Success)
                {
                    str = variableMatch.Groups[1].Value;
                }
                DockerImage img = new DockerImage();
                var match = Regex.Match(str, "([^/]*/)?([^:]+):?(.*)");
                if (!match.Success)
                    throw new FormatException();
                img.User = match.Groups[1].Length == 0 ? string.Empty : match.Groups[1].Value.Substring(0, match.Groups[1].Value.Length - 1);
                img.Name = match.Groups[2].Value;
                img.Tag = match.Groups[3].Value;
                if (img.Tag == string.Empty)
                    img.Tag = "latest";
                return img;
            }
            public override string ToString()
            {
                return ToString(true);
            }
            public string ToString(bool includeTag)
            {
                StringBuilder builder = new StringBuilder();
                if (!String.IsNullOrWhiteSpace(User))
                    builder.Append($"{User}/");
                builder.Append($"{Name}");
                if (includeTag)
                {
                    if (!String.IsNullOrWhiteSpace(Tag))
                        builder.Append($":{Tag}");
                }
                return builder.ToString();
            }
        }

        /// <summary>
        /// This test check that we don't forget to bump one image in both docker-compose.altcoins.yml and docker-compose.yml
        /// </summary>
        [Fact]
        [Trait("Fast", "Fast")]
        public void CheckDockerComposeUpToDate()
        {
            var compose1 = File.ReadAllText(Path.Combine(TestUtils.TryGetSolutionDirectoryInfo().FullName, "BTCPayServer.Tests", "docker-compose.yml"));
            var compose2 = File.ReadAllText(Path.Combine(TestUtils.TryGetSolutionDirectoryInfo().FullName, "BTCPayServer.Tests", "docker-compose.altcoins.yml"));

            List<DockerImage> GetImages(string content)
            {
                List<DockerImage> images = new List<DockerImage>();
                foreach (var line in content.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var l = line.Trim();
                    if (l.StartsWith("image:", StringComparison.OrdinalIgnoreCase))
                    {
                        images.Add(DockerImage.Parse(l.Substring("image:".Length).Trim()));
                    }
                }
                return images;
            }

            var img1 = GetImages(compose1);
            var img2 = GetImages(compose2);
            var groups = img1.Concat(img2).GroupBy(g => g.Name);
            foreach (var g in groups)
            {
                var tags = new HashSet<String>(g.Select(o => o.Tag));
                if (tags.Count != 1)
                {
                    Assert.False(true, $"All docker images '{g.Key}' in docker-compose.yml and docker-compose.altcoins.yml should have the same tags. (Found {string.Join(',', tags)})");
                }
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParsePaymentMethodId()
        {
            var id = PaymentMethodId.Parse("BTC");
            var id1 = PaymentMethodId.Parse("BTC-OnChain");
            var id2 = PaymentMethodId.Parse("BTC-BTCLike");
            Assert.Equal(id, id1);
            Assert.Equal(id, id2);
            Assert.Equal("BTC", id.ToString());
            Assert.Equal("BTC", id.ToString());
            id = PaymentMethodId.Parse("LTC");
            Assert.Equal("LTC", id.ToString());
            Assert.Equal("LTC", id.ToStringNormalized());
            id = PaymentMethodId.Parse("LTC-offchain");
            id1 = PaymentMethodId.Parse("LTC-OffChain");
            id2 = PaymentMethodId.Parse("LTC-LightningLike");
            Assert.Equal(id, id1);
            Assert.Equal(id, id2);
            Assert.Equal("LTC_LightningLike", id.ToString());
            Assert.Equal("LTC-LightningNetwork", id.ToStringNormalized());
#if ALTCOINS
            id = PaymentMethodId.Parse("XMR");
            id1 = PaymentMethodId.Parse("XMR-MoneroLike");
            Assert.Equal(id, id1);
            Assert.Equal("XMR_MoneroLike", id.ToString());
            Assert.Equal("XMR", id.ToStringNormalized());
#endif
        }

        [Fact]
        [Trait("Fast", "Fast")]
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

        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CheckExternalNoReferrerLinks()
        {
            var views = Path.Combine(TestUtils.TryGetSolutionDirectoryInfo().FullName, "BTCPayServer", "Views");
            var viewFiles = Directory.EnumerateFiles(views, "*.cshtml", SearchOption.AllDirectories).ToArray();
            Assert.NotEmpty(viewFiles);

            foreach (var file in viewFiles)
            {
                var html = await File.ReadAllTextAsync(file);

                CheckHtmlNodesForReferrer(file, html, "a", "href");
                CheckHtmlNodesForReferrer(file, html, "form", "action");
            }
        }
        private String GetAttributeValue(String nodeHtml, string attribute)
        {
            Regex regex = new Regex("\\s" + attribute + "=\"(.*?)\"");
            var match = regex.Match(nodeHtml);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }
        private void CheckHtmlNodesForReferrer(string filePath, string html, string tagName, string attribute)
        {
            Regex aNodeRegex = new Regex("<" + tagName + "\\s.*?>");
            var matches = aNodeRegex.Matches(html).OfType<Match>();

            foreach (var match in matches)
            {
                var node = match.Groups[0].Value;
                var attributeValue = GetAttributeValue(node, attribute);

                if (attributeValue != null)
                {
                    if (attributeValue.Length == 0 || attributeValue.StartsWith("mailto:") || attributeValue.StartsWith("/") || attributeValue.StartsWith("~/") || attributeValue.StartsWith("#") || attributeValue.StartsWith("?") || attributeValue.StartsWith("javascript:") || attributeValue.StartsWith("@Url.Action("))
                    {
                        // Local link, this is fine
                    }
                    else if (attributeValue.StartsWith("http://") || attributeValue.StartsWith("https://") ||
                             attributeValue.StartsWith("@"))
                    {
                        // This can be an external link. Treating it as such.
                        var rel = GetAttributeValue(node, "rel");

                        // Building the file path + line number helps us to navigate to the wrong HTML quickly!
                        var lineNumber = html.Substring(0, html.IndexOf(node, StringComparison.InvariantCulture)).Split("\n").Length;
                        Assert.True(rel != null, "Template file \"" + filePath + ":" + lineNumber + "\" contains a possibly external link (" + node + ") that is missing rel=\"noreferrer noopener\"");

                        if (rel != null)
                        {
                            // All external links should have 'rel="noreferrer noopener"'
                            var relWords = rel.Split(" ");
                            Assert.Contains("noreferrer", relWords);
                            Assert.Contains("noopener", relWords);
                        }
                    }
                }
            }
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

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                request.Headers.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:75.0) Gecko/20100101 Firefox/75.0");
                var response = await httpClient.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable) // Temporary issue
                {
                    TestLogs.LogInformation($"Unavailable: {url} ({file})");
                    return;
                }
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
            catch (Exception ex)
            {
                var details = ex is EqualException ? (ex as EqualException).Actual : ex.Message;
                TestLogs.LogInformation($"FAILED: {url} ({file}) {details}");

                throw;
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanHandleUriValidation()
        {
            var attribute = new UriAttribute();
            Assert.True(attribute.IsValid("http://localhost"));
            Assert.True(attribute.IsValid("http://localhost:1234"));
            Assert.True(attribute.IsValid("https://localhost"));
            Assert.True(attribute.IsValid("https://127.0.0.1"));
            Assert.True(attribute.IsValid("http://127.0.0.1"));
            Assert.True(attribute.IsValid("http://127.0.0.1:1234"));
            Assert.True(attribute.IsValid("http://gozo.com"));
            Assert.True(attribute.IsValid("https://gozo.com"));
            Assert.True(attribute.IsValid("https://gozo.com:1234"));
            Assert.True(attribute.IsValid("https://gozo.com:1234/test.css"));
            Assert.True(attribute.IsValid("https://gozo.com:1234/test.png"));
            Assert.False(attribute.IsValid(
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud e"));
            Assert.False(attribute.IsValid(2));
            Assert.False(attribute.IsValid("http://"));
            Assert.False(attribute.IsValid("httpdsadsa.com"));
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParseTorrc()
        {
            var nl = "\n";
            var input = "# For the hidden service BTCPayServer" + nl +
                        "HiddenServiceDir /var/lib/tor/hidden_services/BTCPayServer" + nl +
                        "# Redirecting to nginx" + nl +
                        "HiddenServicePort 80 172.19.0.10:81";
            nl = Environment.NewLine;
            var expected = "HiddenServiceDir /var/lib/tor/hidden_services/BTCPayServer" + nl +
                           "HiddenServicePort 80 172.19.0.10:81" + nl;
            Assert.True(Torrc.TryParse(input, out var torrc));
            Assert.Equal(expected, torrc.ToString());
            nl = "\r\n";
            input = "# For the hidden service BTCPayServer" + nl +
                    "HiddenServiceDir /var/lib/tor/hidden_services/BTCPayServer" + nl +
                    "# Redirecting to nginx" + nl +
                    "HiddenServicePort 80 172.19.0.10:81";

            Assert.True(Torrc.TryParse(input, out torrc));
            Assert.Equal(expected, torrc.ToString());

            input = "# For the hidden service BTCPayServer" + nl +
                    "HiddenServiceDir /var/lib/tor/hidden_services/BTCPayServer" + nl +
                    "# Redirecting to nginx" + nl +
                    "HiddenServicePort 80 172.19.0.10:80" + nl +
                    "HiddenServiceDir /var/lib/tor/hidden_services/Woocommerce" + nl +
                    "# Redirecting to nginx" + nl +
                    "HiddenServicePort 80 172.19.0.11:80";
            nl = Environment.NewLine;
            expected = "HiddenServiceDir /var/lib/tor/hidden_services/BTCPayServer" + nl +
                       "HiddenServicePort 80 172.19.0.10:80" + nl +
                       "HiddenServiceDir /var/lib/tor/hidden_services/Woocommerce" + nl +
                       "HiddenServicePort 80 172.19.0.11:80" + nl;
            Assert.True(Torrc.TryParse(input, out torrc));
            Assert.Equal(expected, torrc.ToString());
        }
#if ALTCOINS
        [Fact]
        [Trait("Fast", "Fast")]
        public void CanCalculateCryptoDue()
        {
            var networkProvider = new BTCPayNetworkProvider(ChainName.Regtest);
            var paymentMethodHandlerDictionary = new PaymentMethodHandlerDictionary(new IPaymentMethodHandler[]
            {
                new BitcoinLikePaymentHandler(null, networkProvider, null, null, null),
                new LightningLikePaymentHandler(null, null, networkProvider, null, null, null),
            });
            var entity = new InvoiceEntity();
            entity.Networks = networkProvider;
#pragma warning disable CS0618
            entity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            entity.SetPaymentMethod(new PaymentMethod()
            {
                CryptoCode = "BTC",
                Rate = 5000,
                NextNetworkFee = Money.Coins(0.1m)
            });
            entity.Price = 5000;

            var paymentMethod = entity.GetPaymentMethods().TryGet("BTC", PaymentTypes.BTCLike);
            var accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(1.1m), accounting.Due);
            Assert.Equal(Money.Coins(1.1m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Output = new TxOut(Money.Coins(0.5m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });

            accounting = paymentMethod.Calculate();
            //Since we need to spend one more txout, it should be 1.1 - 0,5 + 0.1
            Assert.Equal(Money.Coins(0.7m), accounting.Due);
            Assert.Equal(Money.Coins(1.2m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Output = new TxOut(Money.Coins(0.2m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });

            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(0.6m), accounting.Due);
            Assert.Equal(Money.Coins(1.3m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Output = new TxOut(Money.Coins(0.6m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });

            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.3m), accounting.TotalDue);

            entity.Payments.Add(
                new PaymentEntity() { Output = new TxOut(Money.Coins(0.2m), new Key()), Accounted = true });

            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.3m), accounting.TotalDue);

            entity = new InvoiceEntity();
            entity.Networks = networkProvider;
            entity.Price = 5000;
            PaymentMethodDictionary paymentMethods = new PaymentMethodDictionary();
            paymentMethods.Add(
                new PaymentMethod() { CryptoCode = "BTC", Rate = 1000, NextNetworkFee = Money.Coins(0.1m) });
            paymentMethods.Add(
                new PaymentMethod() { CryptoCode = "LTC", Rate = 500, NextNetworkFee = Money.Coins(0.01m) });
            entity.SetPaymentMethods(paymentMethods);
            entity.Payments = new List<PaymentEntity>();
            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(5.1m), accounting.Due);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();

            Assert.Equal(Money.Coins(10.01m), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                CryptoCode = "BTC",
                Output = new TxOut(Money.Coins(1.0m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(4.2m), accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(1.0m), accounting.Paid);
            Assert.Equal(Money.Coins(5.2m), accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2 - 2.0m /* 8.21m */), accounting.Due);
            Assert.Equal(Money.Coins(0.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(2.0m), accounting.Paid);
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2), accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                CryptoCode = "LTC",
                Output = new TxOut(Money.Coins(1.0m), new Key()),
                Accounted = true,
                NetworkFee = 0.01m
            });

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(4.2m - 0.5m + 0.01m / 2), accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(1.5m), accounting.Paid);
            Assert.Equal(Money.Coins(5.2m + 0.01m / 2), accounting.TotalDue); // The fee for LTC added
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(8.21m - 1.0m + 0.01m), accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(3.0m), accounting.Paid);
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2 + 0.01m), accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            var remaining = Money.Coins(4.2m - 0.5m + 0.01m / 2);
            entity.Payments.Add(new PaymentEntity()
            {
                CryptoCode = "BTC",
                Output = new TxOut(remaining, new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.0m) + remaining, accounting.CryptoPaid);
            Assert.Equal(Money.Coins(1.5m) + remaining, accounting.Paid);
            Assert.Equal(Money.Coins(5.2m + 0.01m / 2), accounting.TotalDue);
            Assert.Equal(accounting.Paid, accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Zero, accounting.Due);
            Assert.Equal(Money.Coins(1.0m), accounting.CryptoPaid);
            Assert.Equal(Money.Coins(3.0m) + remaining * 2, accounting.Paid);
            // Paying 2 BTC fee, LTC fee removed because fully paid
            Assert.Equal(Money.Coins(10.01m + 0.1m * 2 + 0.1m * 2 /* + 0.01m no need to pay this fee anymore */),
                accounting.TotalDue);
            Assert.Equal(1, accounting.TxRequired);
            Assert.Equal(accounting.Paid, accounting.TotalDue);
#pragma warning restore CS0618
        }
#endif

        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CheckJsContent()
        {
            // This test verify that no malicious js is added in the minified files.
            // We should extend the tests to other js files, but we can do as we go...

            using HttpClient client = new HttpClient();
            var actual = GetFileContent("BTCPayServer", "wwwroot", "vendor", "bootstrap", "bootstrap.bundle.min.js");
            var version = Regex.Match(actual, "Bootstrap v([0-9]+.[0-9]+.[0-9]+)").Groups[1].Value;
            var expected = await (await client.GetAsync($"https://cdn.jsdelivr.net/npm/bootstrap@{version}/dist/js/bootstrap.bundle.min.js")).Content.ReadAsStringAsync();
            Assert.Equal(expected, actual.Replace("\r\n", "\n", StringComparison.OrdinalIgnoreCase));
        }
        string GetFileContent(params string[] path)
        {
            var l = path.ToList();
            l.Insert(0, TestUtils.TryGetSolutionDirectoryInfo().FullName);
            return File.ReadAllText(Path.Combine(l.ToArray()));
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParseLegacyLabels()
        {
            static void AssertContainsRawLabel(WalletTransactionInfo info)
            {
                foreach (var item in new[] { "blah", "lol", "hello" })
                {
                    Assert.True(info.Labels.ContainsKey(item));
                    var rawLabel = Assert.IsType<RawLabel>(info.Labels[item]);
                    Assert.Equal("raw", rawLabel.Type);
                    Assert.Equal(item, rawLabel.Text);
                }
            }
            var data = new WalletTransactionData();
            data.Labels = "blah,lol,hello,lol";
            var info = data.GetBlobInfo();
            Assert.Equal(3, info.Labels.Count);
            AssertContainsRawLabel(info);
            data.SetBlobInfo(info);
            Assert.Contains("raw", data.Labels);
            Assert.Contains("{", data.Labels);
            Assert.Contains("[", data.Labels);
            info = data.GetBlobInfo();
            AssertContainsRawLabel(info);


            data = new WalletTransactionData()
            {
                Labels = "pos",
                Blob = Encoders.Hex.DecodeData("1f8b08000000000000037abf7b7fb592737e6e6e6a5e89929592522d000000ffff030036bc6ad911000000")
            };
            info = data.GetBlobInfo();
            var label = Assert.Single(info.Labels);
            Assert.Equal("raw", label.Value.Type);
            Assert.Equal("pos", label.Value.Text);
            Assert.Equal("pos", label.Key);


            static void AssertContainsLabel(WalletTransactionInfo info)
            {
                Assert.Equal(2, info.Labels.Count);
                var invoiceLabel = Assert.IsType<ReferenceLabel>(info.Labels["invoice"]);
                Assert.Equal("BFm1MCJPBCDeRoWXvPcwnM", invoiceLabel.Reference);
                Assert.Equal("invoice", invoiceLabel.Text);
                Assert.Equal("invoice", invoiceLabel.Type);

                var appLabel = Assert.IsType<ReferenceLabel>(info.Labels["app"]);
                Assert.Equal("87kj5yKay8mB4UUZcJhZH5TqDKMD3CznjwLjiu1oYZXe", appLabel.Reference);
                Assert.Equal("app", appLabel.Text);
                Assert.Equal("app", appLabel.Type);
            }
            data = new WalletTransactionData()
            {
                Labels = "[\"{\\n  \\\"value\\\": \\\"invoice\\\",\\n  \\\"id\\\": \\\"BFm1MCJPBCDeRoWXvPcwnM\\\"\\n}\",\"{\\n  \\\"value\\\": \\\"app\\\",\\n  \\\"id\\\": \\\"87kj5yKay8mB4UUZcJhZH5TqDKMD3CznjwLjiu1oYZXe\\\"\\n}\"]",
            };
            info = data.GetBlobInfo();
            AssertContainsLabel(info);
            data.SetBlobInfo(info);
            info = data.GetBlobInfo();
            AssertContainsLabel(info);

            static void AssertPayoutLabel(WalletTransactionInfo info)
            {
                Assert.Single(info.Labels);
                var l = Assert.IsType<PayoutLabel>(info.Labels["payout"]);
                Assert.Equal("pullPaymentId", l.PullPaymentId);
                Assert.Equal("walletId", l.WalletId);
                Assert.Equal("payoutId", l.PayoutId);
            }

            var payoutId = "payoutId";
            var pullPaymentId = "pullPaymentId";
            var walletId = "walletId";
            // How it was serialized before

            data = new WalletTransactionData()
            {
                Labels = new JArray(JObject.FromObject(new { value = "payout", id = payoutId, pullPaymentId, walletId })).ToString()
            };
            info = data.GetBlobInfo();
            AssertPayoutLabel(info);
            data.SetBlobInfo(info);
            info = data.GetBlobInfo();
            AssertPayoutLabel(info);
        }



        [Fact]
        [Trait("Fast", "Fast")]
        public void DeterministicUTXOSorter()
        {
            UTXO CreateRandomUTXO()
            {
                return new UTXO() { Outpoint = new OutPoint(RandomUtils.GetUInt256(), RandomUtils.GetUInt32() % 0xff) };
            }
            var comparer = Payments.PayJoin.PayJoinEndpointController.UTXODeterministicComparer.Instance;
            var utxos = Enumerable.Range(0, 100).Select(_ => CreateRandomUTXO()).ToArray();
            Array.Sort(utxos, comparer);
            var utxo53 = utxos[53];
            Array.Sort(utxos, comparer);
            Assert.Equal(utxo53, utxos[53]);
            var utxo54 = utxos[54];
            var utxo52 = utxos[52];
            utxos = utxos.Where((_, i) => i != 53).ToArray();
            Array.Sort(utxos, comparer);
            Assert.Equal(utxo52, utxos[52]);
            Assert.Equal(utxo54, utxos[53]);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanAcceptInvoiceWithTolerance()
        {
            var networkProvider = new BTCPayNetworkProvider(ChainName.Regtest);
            var paymentMethodHandlerDictionary = new PaymentMethodHandlerDictionary(new IPaymentMethodHandler[]
            {
                new BitcoinLikePaymentHandler(null, networkProvider, null, null, null),
                new LightningLikePaymentHandler(null, null, networkProvider, null, null, null),
            });
            var entity = new InvoiceEntity();
            entity.Networks = networkProvider;
#pragma warning disable CS0618
            entity.Payments = new List<PaymentEntity>();
            entity.SetPaymentMethod(new PaymentMethod()
            {
                CryptoCode = "BTC",
                Rate = 5000,
                NextNetworkFee = Money.Coins(0.1m)
            });
            entity.Price = 5000;
            entity.PaymentTolerance = 0;


            var paymentMethod = entity.GetPaymentMethods().TryGet("BTC", PaymentTypes.BTCLike);
            var accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(1.1m), accounting.Due);
            Assert.Equal(Money.Coins(1.1m), accounting.TotalDue);
            Assert.Equal(Money.Coins(1.1m), accounting.MinimumTotalDue);

            entity.PaymentTolerance = 10;
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Coins(0.99m), accounting.MinimumTotalDue);

            entity.PaymentTolerance = 100;
            accounting = paymentMethod.Calculate();
            Assert.Equal(Money.Satoshis(1), accounting.MinimumTotalDue);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanCalculatePeriod()
        {
            Data.PullPaymentData data = new Data.PullPaymentData();
            data.StartDate = Date(0);
            data.EndDate = null;
            var period = data.GetPeriod(Date(1)).Value;
            Assert.Equal(Date(0), period.Start);
            Assert.Null(period.End);
            data.EndDate = Date(7);
            period = data.GetPeriod(Date(1)).Value;
            Assert.Equal(Date(0), period.Start);
            Assert.Equal(Date(7), period.End);
            data.Period = (long)TimeSpan.FromDays(2).TotalSeconds;
            period = data.GetPeriod(Date(1)).Value;
            Assert.Equal(Date(0), period.Start);
            Assert.Equal(Date(2), period.End);
            period = data.GetPeriod(Date(2)).Value;
            Assert.Equal(Date(2), period.Start);
            Assert.Equal(Date(4), period.End);
            period = data.GetPeriod(Date(6)).Value;
            Assert.Equal(Date(6), period.Start);
            Assert.Equal(Date(7), period.End);
            Assert.Null(data.GetPeriod(Date(7)));
            Assert.Null(data.GetPeriod(Date(8)));
            data.EndDate = null;
            period = data.GetPeriod(Date(6)).Value;
            Assert.Equal(Date(6), period.Start);
            Assert.Equal(Date(8), period.End);
            Assert.Null(data.GetPeriod(Date(-1)));
        }

        private DateTimeOffset Date(int days)
        {
            return new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero) + TimeSpan.FromDays(days);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void RoundupCurrenciesCorrectly()
        {
            foreach (var test in new[]
            {
                (0.0005m, "$0.0005 (USD)", "USD"), (0.001m, "$0.001 (USD)", "USD"), (0.01m, "$0.01 (USD)", "USD"),
                (0.1m, "$0.10 (USD)", "USD"), (0.1m, "0,10 € (EUR)", "EUR"), (1000m, "¥1,000 (JPY)", "JPY"),
                (1000.0001m, "₹ 1,000.00 (INR)", "INR"),
                (0.0m, "$0.00 (USD)", "USD")
            })
            {
                var actual = CurrencyNameTable.Instance.DisplayFormatCurrency(test.Item1, test.Item3);
                actual = actual.Replace("￥", "¥"); // Hack so JPY test pass on linux as well
                Assert.Equal(test.Item2, actual);
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CanEnumerateTorServices()
        {
            var tor = new TorServices(new BTCPayNetworkProvider(ChainName.Regtest),
                new OptionsWrapper<BTCPayServerOptions>(new BTCPayServerOptions()
                {
                    TorrcFile = TestUtils.GetTestDataFullPath("Tor/torrc")
                }), BTCPayLogs);
            await tor.Refresh();

            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.BTCPayServer));
            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.P2P));
            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.RPC));
            Assert.True(tor.Services.Count(t => t.ServiceType == TorServiceType.Other) > 1);

            tor = new TorServices(new BTCPayNetworkProvider(ChainName.Regtest),
                new OptionsWrapper<BTCPayServerOptions>(new BTCPayServerOptions()
                {
                    TorrcFile = null,
                    TorServices = "btcpayserver:host.onion:80;btc-p2p:host2.onion:81,BTC-RPC:host3.onion:82,UNKNOWN:host4.onion:83,INVALID:ddd".Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                }), BTCPayLogs);
            await Task.WhenAll(tor.StartAsync(CancellationToken.None));

            var btcpayS = Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.BTCPayServer));
            Assert.Null(btcpayS.Network);
            Assert.Equal("host.onion", btcpayS.OnionHost);
            Assert.Equal(80, btcpayS.VirtualPort);

            var p2p = Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.P2P));
            Assert.NotNull(p2p.Network);
            Assert.Equal("BTC", p2p.Network.CryptoCode);
            Assert.Equal("host2.onion", p2p.OnionHost);
            Assert.Equal(81, p2p.VirtualPort);

            var rpc = Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.RPC));
            Assert.NotNull(p2p.Network);
            Assert.Equal("BTC", rpc.Network.CryptoCode);
            Assert.Equal("host3.onion", rpc.OnionHost);
            Assert.Equal(82, rpc.VirtualPort);

            var unknown = Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.Other));
            Assert.Null(unknown.Network);
            Assert.Equal("host4.onion", unknown.OnionHost);
            Assert.Equal(83, unknown.VirtualPort);
            Assert.Equal("UNKNOWN", unknown.Name);

            Assert.Equal(4, tor.Services.Length);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void ParseDerivationSchemeSettings()
        {
            var mainnet = new BTCPayNetworkProvider(ChainName.Mainnet).GetNetwork<BTCPayNetwork>("BTC");
            var root = new Mnemonic(
                    "usage fever hen zero slide mammal silent heavy donate budget pulse say brain thank sausage brand craft about save attract muffin advance illegal cabbage")
                .DeriveExtKey();

            // ColdCard
            Assert.True(DerivationSchemeSettings.TryParseFromWalletFile(
                "{\"keystore\": {\"ckcc_xpub\": \"xpub661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw\", \"xpub\": \"ypub6WWc2gWwHbdnAAyJDnR4SPL1phRh7REqrPBfZeizaQ1EmTshieRXJC3Z5YoU4wkcdKHEjQGkh6AYEzCQC1Kz3DNaWSwdc1pc8416hAjzqyD\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/0'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}",
                mainnet, out var settings));
            Assert.Equal(root.GetPublicKey().GetHDFingerPrint(), settings.AccountKeySettings[0].RootFingerprint);
            Assert.Equal(settings.AccountKeySettings[0].RootFingerprint,
                HDFingerprint.TryParse("8bafd160", out var hd) ? hd : default);
            Assert.Equal("Coldcard Import 0x60d1af8b", settings.Label);
            Assert.Equal("49'/0'/0'", settings.AccountKeySettings[0].AccountKeyPath.ToString());
            Assert.Equal(
                "ypub6WWc2gWwHbdnAAyJDnR4SPL1phRh7REqrPBfZeizaQ1EmTshieRXJC3Z5YoU4wkcdKHEjQGkh6AYEzCQC1Kz3DNaWSwdc1pc8416hAjzqyD",
                settings.AccountOriginal);
            Assert.Equal(root.Derive(new KeyPath("m/49'/0'/0'")).Neuter().PubKey.WitHash.ScriptPubKey.Hash.ScriptPubKey,
                settings.AccountDerivation.GetDerivation().ScriptPubKey);
            var testnet = new BTCPayNetworkProvider(ChainName.Testnet).GetNetwork<BTCPayNetwork>("BTC");

            // Should be legacy
            Assert.True(DerivationSchemeSettings.TryParseFromWalletFile(
                "{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"tpubDDWYqT3P24znfsaGX7kZcQhNc5LAjnQiKQvUCHF2jS6dsgJBRtymopEU5uGpMaR5YChjuiExZG1X2aTbqXkp82KqH5qnqwWHp6EWis9ZvKr\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/44'/1'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}",
                testnet, out settings));
            Assert.True(settings.AccountDerivation is DirectDerivationStrategy s && !s.Segwit);

            // Should be segwit p2sh
            Assert.True(DerivationSchemeSettings.TryParseFromWalletFile(
                "{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"upub5DSddA9NoRUyJrQ4p86nsCiTSY7kLHrSxx3joEJXjHd4HPARhdXUATuk585FdWPVC2GdjsMePHb6BMDmf7c6KG4K4RPX6LVqBLtDcWpQJmh\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/1'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}",
                testnet, out settings));
            Assert.True(settings.AccountDerivation is P2SHDerivationStrategy p &&
                        p.Inner is DirectDerivationStrategy s2 && s2.Segwit);

            // Should be segwit
            Assert.True(DerivationSchemeSettings.TryParseFromWalletFile(
                "{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"vpub5YjYxTemJ39tFRnuAhwduyxG2tKGjoEpmvqVQRPqdYrqa6YGoeSzBtHXaJUYB19zDbXs3JjbEcVWERjQBPf9bEfUUMZNMv1QnMyHV8JPqyf\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/84'/1'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}",
                testnet, out settings));
            Assert.True(settings.AccountDerivation is DirectDerivationStrategy s3 && s3.Segwit);

            // Specter
            Assert.True(DerivationSchemeSettings.TryParseFromWalletFile(
                "{\"label\": \"Specter\", \"blockheight\": 123456, \"descriptor\": \"wpkh([8bafd160/49h/0h/0h]xpub661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw/0/*)#9x4vkw48\"}",
                mainnet, out var specter));
            Assert.Equal(root.GetPublicKey().GetHDFingerPrint(), specter.AccountKeySettings[0].RootFingerprint);
            Assert.Equal(specter.AccountKeySettings[0].RootFingerprint, hd);
            Assert.Equal("49'/0'/0'", specter.AccountKeySettings[0].AccountKeyPath.ToString());
            Assert.Equal("Specter", specter.Label);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CheckRatesProvider()
        {
            var spy = new SpyRateProvider();
            RateRules.TryParse("X_X = bittrex(X_X);", out var rateRules);

            var factory = CreateBTCPayRateFactory();
            factory.Providers.Clear();
            var fetcher = new RateFetcher(factory);
            factory.Providers.Clear();
            var fetch = new BackgroundFetcherRateProvider(spy);
            fetch.DoNotAutoFetchIfExpired = true;
            factory.Providers.Add("bittrex", fetch);
            var fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules, default).GetAwaiter()
                .GetResult();
            spy.AssertHit();
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules, default).GetAwaiter().GetResult();
            spy.AssertNotHit();
            fetch.UpdateIfNecessary(default).GetAwaiter().GetResult();
            spy.AssertNotHit();
            fetch.RefreshRate = TimeSpan.FromSeconds(1.0);
            Thread.Sleep(1020);
            fetchedRate = fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules, default).GetAwaiter().GetResult();
            spy.AssertNotHit();
            fetch.ValidatyTime = TimeSpan.FromSeconds(1.0);
            fetch.UpdateIfNecessary(default).GetAwaiter().GetResult();
            spy.AssertHit();
            fetch.GetRatesAsync(default).GetAwaiter().GetResult();
            Thread.Sleep(1000);
            Assert.Throws<InvalidOperationException>(() => fetch.GetRatesAsync(default).GetAwaiter().GetResult());
        }

        public static RateProviderFactory CreateBTCPayRateFactory()
        {
            return new RateProviderFactory(TestUtils.CreateHttpFactory());
        }

        class SpyRateProvider : IRateProvider
        {
            public bool Hit { get; set; }

            public Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
            {
                Hit = true;
                var rates = new List<PairRate>();
                rates.Add(new PairRate(CurrencyPair.Parse("BTC_USD"), new BidAsk(5000)));
                return Task.FromResult(rates.ToArray());
            }

            public void AssertHit()
            {
                Assert.True(Hit, "Should have hit the provider");
                Hit = false;
            }

            public void AssertNotHit()
            {
                Assert.False(Hit, "Should have not hit the provider");
                Hit = false;
            }
        }

        [Fact()]
        [Trait("Fast", "Fast")]
        public void CanSolveTheDogesRatesOnKraken()
        {
            var provider = new BTCPayNetworkProvider(ChainName.Mainnet);
            var factory = CreateBTCPayRateFactory();
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
        [Trait("Fast", "Fast")]
        public void CanGetRateCryptoCurrenciesByDefault()
        {
            var provider = new BTCPayNetworkProvider(ChainName.Mainnet);
            var factory = CreateBTCPayRateFactory();
            var fetcher = new RateFetcher(factory);
            var pairs =
                provider.GetAll()
                    .Select(c => new CurrencyPair(c.CryptoCode, "USD"))
                    .ToHashSet();

            var rules = new StoreBlob().GetDefaultRateRules(provider);
            var result = fetcher.FetchRates(pairs, rules, default);
            foreach (var value in result)
            {
                var rateResult = value.Value.GetAwaiter().GetResult();
                TestLogs.LogInformation($"Testing {value.Key.ToString()}");
                if (value.Key.ToString() == "BTX_USD") // Broken shitcoin
                    continue;
                Assert.True(rateResult.BidAsk != null, $"Impossible to get the rate {rateResult.EvaluatedRule}");
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanQueryDirectProviders()
        {
            var factory = CreateBTCPayRateFactory();
            var directlySupported = factory.GetSupportedExchanges().Where(s => s.Source == RateSource.Direct)
                .Select(s => s.Id).ToHashSet();
            var all = string.Join("\r\n", factory.GetSupportedExchanges().Select(e => e.Id).ToArray());
            foreach (var result in factory
                .Providers
                .Where(p => p.Value is BackgroundFetcherRateProvider bf &&
                            !(bf.Inner is CoinGeckoRateProvider cg && cg.UnderlyingExchange != null))
                .Select(p => (ExpectedName: p.Key, ResultAsync: p.Value.GetRatesAsync(default),
                    Fetcher: (BackgroundFetcherRateProvider)p.Value))
                .ToList())
            {
                TestLogs.LogInformation($"Testing {result.ExpectedName}");
                if (result.ExpectedName == "ndax")
                {
                    TestLogs.LogInformation($"Skipping (currently crashing)");
                    continue;
                }

                result.Fetcher.InvalidateCache();
                var exchangeRates = new ExchangeRates(result.ExpectedName, result.ResultAsync.Result);
                result.Fetcher.InvalidateCache();
                Assert.NotNull(exchangeRates);
                Assert.NotEmpty(exchangeRates);
                Assert.NotEmpty(exchangeRates.ByExchange[result.ExpectedName]);
                if (result.ExpectedName == "bitbank" || result.ExpectedName == "bitflyer")
                {
                    Assert.Contains(exchangeRates.ByExchange[result.ExpectedName],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "JPY") &&
                             e.BidAsk.Bid > 100m); // 1BTC will always be more than 100JPY
                }
                else if (result.ExpectedName == "polispay")
                {
                    Assert.Contains(exchangeRates.ByExchange[result.ExpectedName],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "POLIS") &&
                             e.BidAsk.Bid > 1.0m); // 1BTC will always be more than 1 POLIS
                }
                else if (result.ExpectedName == "argoneum")
                {
                    Assert.Contains(exchangeRates.ByExchange[result.ExpectedName],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "AGM") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 AGM
                }
                else if (result.ExpectedName == "ripio")
                {
                    // Ripio keeps changing their pair, so anything is fine...
                    Assert.NotEmpty(exchangeRates.ByExchange[result.ExpectedName]);
                }
                else if (result.ExpectedName == "cryptomarket")
                {
                    Assert.Contains(exchangeRates.ByExchange[result.ExpectedName],
                        e => e.CurrencyPair == new CurrencyPair("BTC", "CLP") &&
                             e.BidAsk.Bid > 1.0m); // 1 BTC will always be more than 1 CLP
                }
                else
                {
                    // This check if the currency pair is using right currency pair
                    Assert.Contains(exchangeRates.ByExchange[result.ExpectedName],
                        e => (e.CurrencyPair == new CurrencyPair("BTC", "USD") ||
                                e.CurrencyPair == new CurrencyPair("BTC", "EUR") ||
                                e.CurrencyPair == new CurrencyPair("BTC", "USDT") ||
                                e.CurrencyPair == new CurrencyPair("BTC", "USDC") ||
                                e.CurrencyPair == new CurrencyPair("BTC", "CAD"))
                                && e.BidAsk.Bid > 1.0m // 1BTC will always be more than 1USD
                    );
                }
                // We are not showing a directly implemented exchange as directly implemented in the UI
                // we need to modify the AvailableRateProvider

                // There are some exception we stopped supporting but don't want to break backward compat
                if (result.ExpectedName != "coinaverage" && result.ExpectedName != "gdax")
                    Assert.Contains(result.ExpectedName, directlySupported);
            }

            // Kraken emit one request only after first GetRates
            factory.Providers["kraken"].GetRatesAsync(default).GetAwaiter().GetResult();
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanExportBackgroundFetcherState()
        {
            var factory = CreateBTCPayRateFactory();
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
        [Trait("Fast", "Fast")]
        public async Task CanExpandExternalConnectionString()
        {
            var unusedUri = new Uri("https://toto.com");
            Assert.True(ExternalConnectionString.TryParse("server=/test", out var connStr, out var error));
            var expanded = await connStr.Expand(new Uri("https://toto.com"), ExternalServiceTypes.Charge,
                ChainName.Mainnet);
            Assert.Equal(new Uri("https://toto.com/test"), expanded.Server);
            expanded = await connStr.Expand(new Uri("http://toto.onion"), ExternalServiceTypes.Charge,
                ChainName.Mainnet);
            Assert.Equal(new Uri("http://toto.onion/test"), expanded.Server);
            await Assert.ThrowsAsync<SecurityException>(() =>
                connStr.Expand(new Uri("http://toto.com"), ExternalServiceTypes.Charge, ChainName.Mainnet));
            await connStr.Expand(new Uri("http://toto.com"), ExternalServiceTypes.Charge, ChainName.Testnet);

            // Make sure absolute paths are not expanded
            Assert.True(ExternalConnectionString.TryParse("server=https://tow/test", out connStr, out error));
            expanded = await connStr.Expand(new Uri("https://toto.com"), ExternalServiceTypes.Charge,
                ChainName.Mainnet);
            Assert.Equal(new Uri("https://tow/test"), expanded.Server);

            // Error if directory not exists
            Assert.True(ExternalConnectionString.TryParse($"server={unusedUri};macaroondirectorypath=pouet",
                out connStr, out error));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                connStr.Expand(unusedUri, ExternalServiceTypes.LNDGRPC, ChainName.Mainnet));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
                connStr.Expand(unusedUri, ExternalServiceTypes.LNDRest, ChainName.Mainnet));
            await connStr.Expand(unusedUri, ExternalServiceTypes.Charge, ChainName.Mainnet);

            var macaroonDirectory = CreateDirectory();
            Assert.True(ExternalConnectionString.TryParse(
                $"server={unusedUri};macaroondirectorypath={macaroonDirectory}", out connStr, out error));
            await connStr.Expand(unusedUri, ExternalServiceTypes.LNDGRPC, ChainName.Mainnet);
            expanded = await connStr.Expand(unusedUri, ExternalServiceTypes.LNDRest, ChainName.Mainnet);
            Assert.NotNull(expanded.Macaroons);
            Assert.Null(expanded.MacaroonFilePath);
            Assert.Null(expanded.Macaroons.AdminMacaroon);
            Assert.Null(expanded.Macaroons.InvoiceMacaroon);
            Assert.Null(expanded.Macaroons.ReadonlyMacaroon);

            File.WriteAllBytes($"{macaroonDirectory}/admin.macaroon", new byte[] { 0xaa });
            File.WriteAllBytes($"{macaroonDirectory}/invoice.macaroon", new byte[] { 0xab });
            File.WriteAllBytes($"{macaroonDirectory}/readonly.macaroon", new byte[] { 0xac });
            expanded = await connStr.Expand(unusedUri, ExternalServiceTypes.LNDRest, ChainName.Mainnet);
            Assert.NotNull(expanded.Macaroons.AdminMacaroon);
            Assert.NotNull(expanded.Macaroons.InvoiceMacaroon);
            Assert.Equal("ab", expanded.Macaroons.InvoiceMacaroon.Hex);
            Assert.Equal(0xab, expanded.Macaroons.InvoiceMacaroon.Bytes[0]);
            Assert.NotNull(expanded.Macaroons.ReadonlyMacaroon);

            Assert.True(ExternalConnectionString.TryParse(
                $"server={unusedUri};cookiefilepath={macaroonDirectory}/charge.cookie", out connStr, out error));
            File.WriteAllText($"{macaroonDirectory}/charge.cookie", "apitoken");
            expanded = await connStr.Expand(unusedUri, ExternalServiceTypes.Charge, ChainName.Mainnet);
            Assert.Equal("apitoken", expanded.APIToken);
        }

        private string CreateDirectory([CallerMemberName] string caller = null)
        {
            var name = $"{caller}-{NBitcoin.RandomUtils.GetUInt32()}";
            Directory.CreateDirectory(name);
            return name;
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanCheckFileNameValid()
        {
            var tests = new[]
            {
                ("test.com", true),
                ("/test.com", false),
                ("te/st.com", false),
                ("\\test.com", false),
                ("te\\st.com", false)
            };
            foreach (var t in tests)
            {
                Assert.Equal(t.Item2, t.Item1.IsValidFileName());
            }
        }

        [Trait("Fast", "Fast")]
        [Fact]
        public void CanFixupWebhookEventPropertyName()
        {
            string legacy = "{\"orignalDeliveryId\":\"blahblah\"}";
            var obj = JsonConvert.DeserializeObject<WebhookEvent>(legacy, WebhookEvent.DefaultSerializerSettings);
            Assert.Equal("blahblah", obj.OriginalDeliveryId);
            var serialized = JsonConvert.SerializeObject(obj, WebhookEvent.DefaultSerializerSettings);
            Assert.DoesNotContain("orignalDeliveryId", serialized);
            Assert.Contains("originalDeliveryId", serialized);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CanCreateSqlitedb()
        {
            if (File.Exists("temp.db"))
                File.Delete("temp.db");
            // This test sqlite can migrate
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            builder.UseSqlite("Data Source=temp.db");
            await new ApplicationDbContext(builder.Options).Database.MigrateAsync();
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanUsePermission()
        {
            Assert.True(Permission.Create(Policies.CanModifyServerSettings)
                .Contains(Permission.Create(Policies.CanModifyServerSettings)));
            Assert.True(Permission.Create(Policies.CanModifyProfile)
                .Contains(Permission.Create(Policies.CanViewProfile)));
            Assert.True(Permission.Create(Policies.CanModifyStoreSettings)
                .Contains(Permission.Create(Policies.CanViewStoreSettings)));
            Assert.False(Permission.Create(Policies.CanViewStoreSettings)
                .Contains(Permission.Create(Policies.CanModifyStoreSettings)));
            Assert.False(Permission.Create(Policies.CanModifyServerSettings)
                .Contains(Permission.Create(Policies.CanModifyStoreSettings)));
            Assert.True(Permission.Create(Policies.Unrestricted)
                .Contains(Permission.Create(Policies.CanModifyStoreSettings)));
            Assert.True(Permission.Create(Policies.Unrestricted)
                .Contains(Permission.Create(Policies.CanModifyStoreSettings, "abc")));

            Assert.True(Permission.Create(Policies.CanViewStoreSettings)
                .Contains(Permission.Create(Policies.CanViewStoreSettings, "abcd")));
            Assert.False(Permission.Create(Policies.CanModifyStoreSettings, "abcd")
                .Contains(Permission.Create(Policies.CanModifyStoreSettings)));
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParseFilter()
        {
            var filter = "storeid:abc, status:abed, blabhbalh ";
            var search = new SearchString(filter);
            Assert.Equal("storeid:abc, status:abed, blabhbalh", search.ToString());
            Assert.Equal("blabhbalh", search.TextSearch);
            Assert.Single(search.Filters["storeid"]);
            Assert.Single(search.Filters["status"]);
            Assert.Equal("abc", search.Filters["storeid"].First());
            Assert.Equal("abed", search.Filters["status"].First());

            filter = "status:abed, status:abed2";
            search = new SearchString(filter);
            Assert.Equal("", search.TextSearch);
            Assert.Equal("status:abed, status:abed2", search.ToString());
            Assert.Throws<KeyNotFoundException>(() => search.Filters["test"]);
            Assert.Equal(2, search.Filters["status"].Count);
            Assert.Equal("abed", search.Filters["status"].First());
            Assert.Equal("abed2", search.Filters["status"].Skip(1).First());

            filter = "StartDate:2019-04-25 01:00 AM, hekki";
            search = new SearchString(filter);
            Assert.Equal("2019-04-25 01:00 AM", search.Filters["startdate"].First());
            Assert.Equal("hekki", search.TextSearch);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParseFingerprint()
        {
            Assert.True(SSH.SSHFingerprint.TryParse("4e343c6fc6cfbf9339c02d06a151e1dd", out var unused));
            Assert.Equal("4e:34:3c:6f:c6:cf:bf:93:39:c0:2d:06:a1:51:e1:dd", unused.ToString());
            Assert.True(SSH.SSHFingerprint.TryParse("4e:34:3c:6f:c6:cf:bf:93:39:c0:2d:06:a1:51:e1:dd", out unused));
            Assert.True(SSH.SSHFingerprint.TryParse("SHA256:Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w", out unused));
            Assert.True(SSH.SSHFingerprint.TryParse("SHA256:Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w=", out unused));
            Assert.True(SSH.SSHFingerprint.TryParse("Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w=", out unused));
            Assert.Equal("SHA256:Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w", unused.ToString());

            Assert.True(SSH.SSHFingerprint.TryParse("Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w=", out var f1));
            Assert.True(SSH.SSHFingerprint.TryParse("SHA256:Wl7CdRgT4u5T7yPMsxSrlFP+HIJJWwidGkzphJ8di5w", out var f2));
            Assert.Equal(f1.ToString(), f2.ToString());
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void HasCurrencyDataForNetworks()
        {
            var btcPayNetworkProvider = new BTCPayNetworkProvider(ChainName.Regtest);
            foreach (var network in btcPayNetworkProvider.GetAll())
            {
                var cd = CurrencyNameTable.Instance.GetCurrencyData(network.CryptoCode, false);
                Assert.NotNull(cd);
                Assert.Equal(network.Divisibility, cd.Divisibility);
                Assert.True(cd.Crypto);
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void SetOrderIdMetadataDoesntConvertInOctal()
        {
            var m = new InvoiceMetadata();
            m.OrderId = "000000161";
            Assert.Equal("000000161", m.OrderId);
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void CanParseCurrencyValue()
        {
            Assert.True(CurrencyValue.TryParse("1.50USD", out var result));
            Assert.Equal("1.50 USD", result.ToString());
            Assert.True(CurrencyValue.TryParse("1.50 USD", out result));
            Assert.Equal("1.50 USD", result.ToString());
            Assert.True(CurrencyValue.TryParse("1.50 usd", out result));
            Assert.Equal("1.50 USD", result.ToString());
            Assert.True(CurrencyValue.TryParse("1 usd", out result));
            Assert.Equal("1 USD", result.ToString());
            Assert.True(CurrencyValue.TryParse("1usd", out result));
            Assert.Equal("1 USD", result.ToString());
            Assert.True(CurrencyValue.TryParse("1.501 usd", out result));
            Assert.Equal("1.50 USD", result.ToString());
            Assert.False(CurrencyValue.TryParse("1.501 WTFF", out result));
            Assert.False(CurrencyValue.TryParse("1,501 usd", out result));
            Assert.False(CurrencyValue.TryParse("1.501", out result));
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CanScheduleBackgroundTasks()
        {
            BackgroundJobClient client = new BackgroundJobClient(BTCPayLogs);
            MockDelay mockDelay = new MockDelay();
            client.Delay = mockDelay;
            bool[] jobs = new bool[4];
            TestLogs.LogInformation("Start Job[0] in 5 sec");
            client.Schedule((_) =>
            {
                TestLogs.LogInformation("Job[0]");
                jobs[0] = true;
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(5.0));
            TestLogs.LogInformation("Start Job[1] in 2 sec");
            client.Schedule((_) =>
            {
                TestLogs.LogInformation("Job[1]");
                jobs[1] = true;
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(2.0));
            TestLogs.LogInformation("Start Job[2] fails in 6 sec");
            client.Schedule((_) =>
            {
                jobs[2] = true;
                throw new Exception("Job[2]");
            }, TimeSpan.FromSeconds(6.0));
            TestLogs.LogInformation("Start Job[3] starts in in 7 sec");
            client.Schedule((_) =>
            {
                TestLogs.LogInformation("Job[3]");
                jobs[3] = true;
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(7.0));

            Assert.True(new[] { false, false, false, false }.SequenceEqual(jobs));
            CancellationTokenSource cts = new CancellationTokenSource();
            var processing = client.ProcessJobs(cts.Token);

            Assert.Equal(4, client.GetExecutingCount());

            var waitJobsFinish = client.WaitAllRunning(default);

            await mockDelay.Advance(TimeSpan.FromSeconds(2.0));
            Assert.True(new[] { false, true, false, false }.SequenceEqual(jobs));

            await mockDelay.Advance(TimeSpan.FromSeconds(3.0));
            Assert.True(new[] { true, true, false, false }.SequenceEqual(jobs));

            await mockDelay.Advance(TimeSpan.FromSeconds(1.0));
            Assert.True(new[] { true, true, true, false }.SequenceEqual(jobs));
            Assert.Equal(1, client.GetExecutingCount());

            Assert.False(waitJobsFinish.Wait(1));
            Assert.False(waitJobsFinish.IsCompletedSuccessfully);

            await mockDelay.Advance(TimeSpan.FromSeconds(1.0));
            Assert.True(new[] { true, true, true, true }.SequenceEqual(jobs));

            await waitJobsFinish;
            Assert.True(waitJobsFinish.IsCompletedSuccessfully);
            Assert.True(!waitJobsFinish.IsFaulted);
            Assert.Equal(0, client.GetExecutingCount());

            bool jobExecuted = false;
            TestLogs.LogInformation("This job will be cancelled");
            client.Schedule((_) =>
            {
                jobExecuted = true;
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(1.0));
            await mockDelay.Advance(TimeSpan.FromSeconds(0.5));
            Assert.False(jobExecuted);
            TestUtils.Eventually(() => Assert.Equal(1, client.GetExecutingCount()));


            waitJobsFinish = client.WaitAllRunning(default);
            Assert.False(waitJobsFinish.Wait(100));
            cts.Cancel();
            await waitJobsFinish;
            Assert.True(waitJobsFinish.Wait(1));
            Assert.True(waitJobsFinish.IsCompletedSuccessfully);
            Assert.False(waitJobsFinish.IsFaulted);
            Assert.False(jobExecuted);

            await mockDelay.Advance(TimeSpan.FromSeconds(1.0));

            Assert.False(jobExecuted);
            Assert.Equal(0, client.GetExecutingCount());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await processing);
            Assert.True(processing.IsCanceled);
            Assert.True(client.WaitAllRunning(default).Wait(100));
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public void PosDataParser_ParsesCorrectly()
        {
            var testCases =
                new List<(string input, Dictionary<string, object> expectedOutput)>()
                {
                    {(null, new Dictionary<string, object>())},
                    {("", new Dictionary<string, object>())},
                    {("{}", new Dictionary<string, object>())},
                    {("non-json-content", new Dictionary<string, object>() {{string.Empty, "non-json-content"}})},
                    {("[1,2,3]", new Dictionary<string, object>() {{string.Empty, "[1,2,3]"}})},
                    {("{ \"key\": \"value\"}", new Dictionary<string, object>() {{"key", "value"}})},
                    {("{ \"key\": true}", new Dictionary<string, object>() {{"key", "True"}})},
                    {
                        ("{ invalidjson file here}",
                            new Dictionary<string, object>() {{String.Empty, "{ invalidjson file here}"}})
                    },
                    // Duplicate keys should not crash things
                    {("{ \"key\": true, \"key\": true}", new Dictionary<string, object>() {{"key", "True"}})}
                };

            testCases.ForEach(tuple =>
            {
                Assert.Equal(tuple.expectedOutput, InvoiceController.PosDataParser.ParsePosData(tuple.input));
            });
        }
    }
}
