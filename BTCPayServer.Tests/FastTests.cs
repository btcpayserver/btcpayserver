using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Hosting;
using BTCPayServer.JsonConverters;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins;
using BTCPayServer.Plugins.Bitcoin;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Fees;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Scripting.Parser;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Tests
{
    [Trait("Fast", "Fast")]
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
                    Assert.Fail($"All docker images '{g.Key}' in docker-compose.yml and docker-compose.altcoins.yml should have the same tags. (Found {string.Join(',', tags)})");
                }
            }
        }


        [Fact]
        public void CanParseDecimals()
        {
            CanParseDecimalsCore("{\"qty\": 1}", 1.0m);
            CanParseDecimalsCore("{\"qty\": \"1\"}", 1.0m);
            CanParseDecimalsCore("{\"qty\": 1.0}", 1.0m);
            CanParseDecimalsCore("{\"qty\": \"1.0\"}", 1.0m);
            CanParseDecimalsCore("{\"qty\": 6.1e-7}", 6.1e-7m);
            CanParseDecimalsCore("{\"qty\": \"6.1e-7\"}", 6.1e-7m);

            var data = JsonConvert.DeserializeObject<TradeRequestData>("{\"qty\": \"6.1e-7\", \"fromAsset\":\"Test\"}");
            Assert.Equal(6.1e-7m, data.Qty.Value);
            Assert.Equal("Test", data.FromAsset);
            data = JsonConvert.DeserializeObject<TradeRequestData>("{\"fromAsset\":\"Test\", \"qty\": \"6.1e-7\"}");
            Assert.Equal(6.1e-7m, data.Qty.Value);
            Assert.Equal("Test", data.FromAsset);
        }

        [Fact]
        public void CanInterpolateOrBound()
        {
            var testData = new ((int Blocks, decimal Fee)[] Data, int Target, decimal Expected) []
            {
                ([(0, 0m), (10, 100m)], 5, 50m),
                ([(50, 0m), (100, 100m)], 5, 0.0m),
                ([(50, 0m), (100, 100m)], 101, 100.0m),
                ([(50, 100m), (50, 100m)], 101, 100.0m),
                ([(50, 0m), (100, 100m)], 75, 50m),
                ([(0, 0m), (50, 50m), (100, 100m)], 75, 75m),
                ([(0, 0m), (500, 50m), (1000, 100m)], 750, 75m),
                ([(0, 0m), (500, 50m), (1000, 100m)], 100, 10m),
                ([(0, 0m), (100, 100m)], 80, 80m),
                ([(0, 0m), (100, 100m)], 25, 25m),
                ([(0, 0m), (25, 25m), (50, 50m), (100, 100m), (110, 120m)], 75, 75m),
                ([(0, 0m), (25, 0m), (50, 50m), (100, 100m), (110, 0m)], 75, 75m),
                ([(0, 0m), (25, 0m), (50, 50m), (100, 100m), (110, 0m)], 50, 50m),
                ([(0, 0m), (25, 0m), (50, 50m), (100, 100m), (110, 0m)], 100, 100m),
                ([(0, 0m), (25, 0m), (50, 50m), (100, 100m), (110, 0m)], 102, 80m),
            };
            foreach (var t in testData)
            {
                var actual = MempoolSpaceFeeProvider.InterpolateOrBound(t.Data.Select(t => new MempoolSpaceFeeProvider.BlockFeeRate(t.Blocks, new FeeRate(t.Fee))).ToArray(), t.Target);
                Assert.Equal(new FeeRate(t.Expected), actual);
            }
        }
        [Fact]
        public void CanRandomizeByPercentage()
        {
            var generated = Enumerable.Range(0, 1000).Select(_ => MempoolSpaceFeeProvider.RandomizeByPercentage(100.0m, 10.0m)).ToArray();
            Assert.Empty(generated.Where(g => g < 90m));
            Assert.Empty(generated.Where(g => g > 110m));
            Assert.NotEmpty(generated.Where(g => g < 91m));
            Assert.NotEmpty(generated.Where(g => g > 109m));
        }

        private void CanParseDecimalsCore(string str, decimal expected)
        {
            var d = JsonConvert.DeserializeObject<LedgerEntryData>(str);
            Assert.Equal(expected, d.Qty);
            var d2 = JsonConvert.DeserializeObject<TradeRequestData>(str);
            Assert.Equal(new TradeQuantity(expected, TradeQuantity.ValueType.Exact), d2.Qty);
        }

        [Fact]
        public void CanMergeReceiptOptions()
        {
            var r = InvoiceDataBase.ReceiptOptions.Merge(null, null);
            Assert.True(r?.Enabled);
            Assert.True(r?.ShowPayments);
            Assert.True(r?.ShowQR);

            r = InvoiceDataBase.ReceiptOptions.Merge(new InvoiceDataBase.ReceiptOptions(), null);
            Assert.True(r?.Enabled);
            Assert.True(r?.ShowPayments);
            Assert.True(r?.ShowQR);

            r = InvoiceDataBase.ReceiptOptions.Merge(new InvoiceDataBase.ReceiptOptions() { Enabled = false }, null);
            Assert.False(r?.Enabled);
            Assert.True(r?.ShowPayments);
            Assert.True(r?.ShowQR);

            r = InvoiceDataBase.ReceiptOptions.Merge(new InvoiceDataBase.ReceiptOptions() { Enabled = false, ShowQR = false }, new InvoiceDataBase.ReceiptOptions() { Enabled = true });
            Assert.True(r?.Enabled);
            Assert.True(r?.ShowPayments);
            Assert.False(r?.ShowQR);

            StoreBlob blob = new StoreBlob();
            Assert.True(blob.ReceiptOptions.Enabled);
            blob = JsonConvert.DeserializeObject<StoreBlob>("{}");
            Assert.True(blob.ReceiptOptions.Enabled);
            blob = JsonConvert.DeserializeObject<StoreBlob>("{\"receiptOptions\":{\"enabled\": false}}");
            Assert.False(blob.ReceiptOptions.Enabled);
            blob = JsonConvert.DeserializeObject<StoreBlob>(JsonConvert.SerializeObject(blob));
            Assert.False(blob.ReceiptOptions.Enabled);
        }

        [Fact]
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
                    else if (attributeValue.StartsWith("http://") || attributeValue.StartsWith("https://"))
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

        [Fact]
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

        [Fact]
        public void CanParseCartItems()
        {
            Assert.True(AppService.TryParsePosCartItems(new JObject()
            {
                {"cart", new JArray()
                {
                    new JObject()
                    {
                        { "id", "ddd"},
                        {"price", 4},
                        {"count", 1}
                    }
                }}
            }, out var items));
            Assert.Equal("ddd", items[0].Id);
            Assert.Equal(1, items[0].Count);
            Assert.Equal(4, items[0].Price);

            // Using legacy parsing
            Assert.True(AppService.TryParsePosCartItems(new JObject()
            {
                {"cart", new JArray()
                {
                    new JObject()
                    {
                        { "id", "ddd"},
                        {"price", new JObject()
                            {
                                { "value", 8.49m }
                            }
                        },
                        {"count", 1}
                    }
                }}
            }, out items));
            Assert.Equal("ddd", items[0].Id);
            Assert.Equal(1, items[0].Count);
            Assert.Equal(8.49m, items[0].Price);

            Assert.False(AppService.TryParsePosCartItems(new JObject()
            {
                {"cart", new JArray()
                {
                    new JObject()
                    {
                        { "id", "ddd"},
                        {"price", new JObject()
                            {
                                { "value", "nocrahs" }
                            }
                        },
                        {"count", 1}
                    }
                }}
            }, out items));
        }

        [Fact]
        public void CanCalculateDust()
        {
            var entity = new InvoiceEntity() { Currency = "USD" };
            entity.Networks = CreateNetworkProvider(ChainName.Regtest);
#pragma warning disable CS0618
            entity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            entity.SetPaymentMethod(new PaymentMethod()
            {
                Currency = "BTC",
                Rate = 34_000m
            });
            entity.Price = 4000;
            entity.UpdateTotals();
            var accounting = entity.GetPaymentMethods().First().Calculate();
            // Exact price should be 0.117647059..., but the payment method round up to one sat
            Assert.Equal(0.11764706m, accounting.Due);
            entity.Payments.Add(new PaymentEntity()
            {
                Currency = "BTC",
                Output = new TxOut(Money.Coins(0.11764706m), new Key()),
                Accounted = true
            });
            entity.UpdateTotals();
            Assert.Equal(0.0m, entity.NetDue);
            // The dust's value is below 1 sat
            Assert.True(entity.Dust > 0.0m);
            Assert.True(Money.Satoshis(1.0m).ToDecimal(MoneyUnit.BTC) * entity.Rates["BTC"] > entity.Dust);
            Assert.True(!entity.IsOverPaid);
            Assert.True(!entity.IsUnderPaid);

            // Now, imagine there is litecoin. It might seem from its
            // perspecitve that there has been a slight over payment.
            // However, Calculate() should just cap it to 0.0m
            entity.SetPaymentMethod(new PaymentMethod()
            {
                Currency = "LTC",
                Rate = 3400m
            });
            entity.UpdateTotals();
            var method = entity.GetPaymentMethods().First(p => p.Currency == "LTC");
            accounting = method.Calculate();
            Assert.Equal(0.0m, accounting.DueUncapped);

#pragma warning restore CS0618
        }
#if ALTCOINS
        [Fact]
        public void CanCalculateCryptoDue()
        {
            var networkProvider = CreateNetworkProvider(ChainName.Regtest);
            var entity = new InvoiceEntity() { Currency = "USD" };
            entity.Networks = networkProvider;
#pragma warning disable CS0618
            entity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            entity.SetPaymentMethod(new PaymentMethod()
            {
                Currency = "BTC",
                Rate = 5000,
                NextNetworkFee = Money.Coins(0.1m)
            });
            entity.Price = 5000;
            entity.UpdateTotals();

            var paymentMethod = entity.GetPaymentMethods().TryGet("BTC", PaymentTypes.BTCLike);
            var accounting = paymentMethod.Calculate();
            Assert.Equal(1.0m, accounting.ToSmallestUnit(Money.Satoshis(1.0m).ToDecimal(MoneyUnit.BTC)));
            Assert.Equal(1.1m, accounting.Due);
            Assert.Equal(1.1m, accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Currency = "BTC",
                Output = new TxOut(Money.Coins(0.5m), new Key()),
                Rate = 5000,
                Accounted = true,
                NetworkFee = 0.1m
            });
            entity.UpdateTotals();
            accounting = paymentMethod.Calculate();
            //Since we need to spend one more txout, it should be 1.1 - 0,5 + 0.1
            Assert.Equal(0.7m, accounting.Due);
            Assert.Equal(1.2m, accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Currency = "BTC",
                Output = new TxOut(Money.Coins(0.2m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });
            entity.UpdateTotals();
            accounting = paymentMethod.Calculate();
            Assert.Equal(0.6m, accounting.Due);
            Assert.Equal(1.3m, accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Currency = "BTC",
                Output = new TxOut(Money.Coins(0.6m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });
            entity.UpdateTotals();
            accounting = paymentMethod.Calculate();
            Assert.Equal(0.0m, accounting.Due);
            Assert.Equal(1.3m, accounting.TotalDue);

            entity.Payments.Add(
                new PaymentEntity() { Currency = "BTC", Output = new TxOut(Money.Coins(0.2m), new Key()), Accounted = true });
            entity.UpdateTotals();
            accounting = paymentMethod.Calculate();
            Assert.Equal(0.0m, accounting.Due);
            Assert.Equal(1.3m, accounting.TotalDue);

            entity = new InvoiceEntity();
            entity.Networks = networkProvider;
            entity.Price = 5000;
            PaymentMethodDictionary paymentMethods = new PaymentMethodDictionary();
            paymentMethods.Add(
                new PaymentMethod() { Currency = "BTC", Rate = 1000, NextNetworkFee = Money.Coins(0.1m) });
            paymentMethods.Add(
                new PaymentMethod() { Currency = "LTC", Rate = 500, NextNetworkFee = Money.Coins(0.01m) });
            entity.SetPaymentMethods(paymentMethods);
            entity.Payments = new List<PaymentEntity>();
            entity.UpdateTotals();
            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(5.1m, accounting.Due);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();

            Assert.Equal(10.01m, accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Currency = "BTC",
                Output = new TxOut(Money.Coins(1.0m), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });
            entity.UpdateTotals();
            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(4.2m, accounting.Due);
            Assert.Equal(1.0m, accounting.CryptoPaid);
            Assert.Equal(1.0m, accounting.Paid);
            Assert.Equal(5.2m, accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(10.01m + 0.1m * 2 - 2.0m /* 8.21m */, accounting.Due);
            Assert.Equal(0.0m, accounting.CryptoPaid);
            Assert.Equal(2.0m, accounting.Paid);
            Assert.Equal(10.01m + 0.1m * 2, accounting.TotalDue);

            entity.Payments.Add(new PaymentEntity()
            {
                Currency = "LTC",
                Output = new TxOut(Money.Coins(1.0m), new Key()),
                Accounted = true,
                NetworkFee = 0.01m
            });
            entity.UpdateTotals();
            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(4.2m - 0.5m + 0.01m / 2, accounting.Due);
            Assert.Equal(1.0m, accounting.CryptoPaid);
            Assert.Equal(1.5m, accounting.Paid);
            Assert.Equal(5.2m + 0.01m / 2, accounting.TotalDue); // The fee for LTC added
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(8.21m - 1.0m + 0.01m, accounting.Due);
            Assert.Equal(1.0m, accounting.CryptoPaid);
            Assert.Equal(3.0m, accounting.Paid);
            Assert.Equal(10.01m + 0.1m * 2 + 0.01m, accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            var remaining = Money.Coins(4.2m - 0.5m + 0.01m / 2.0m).ToDecimal(MoneyUnit.BTC);
            entity.Payments.Add(new PaymentEntity()
            {
                Currency = "BTC",
                Output = new TxOut(Money.Coins(remaining), new Key()),
                Accounted = true,
                NetworkFee = 0.1m
            });
            entity.UpdateTotals();
            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(0.0m, accounting.Due);
            Assert.Equal(1.0m + remaining, accounting.CryptoPaid);
            Assert.Equal(1.5m + remaining, accounting.Paid);
            Assert.Equal(5.2m + 0.01m / 2, accounting.TotalDue);
            Assert.Equal(accounting.Paid, accounting.TotalDue);
            Assert.Equal(2, accounting.TxRequired);

            paymentMethod = entity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = paymentMethod.Calculate();
            Assert.Equal(0.0m, accounting.Due);
            Assert.Equal(1.0m, accounting.CryptoPaid);
            Assert.Equal(3.0m + remaining * 2, accounting.Paid);
            // Paying 2 BTC fee, LTC fee removed because fully paid
            Assert.Equal(10.01m + 0.1m * 2 + 0.1m * 2 /* + 0.01m no need to pay this fee anymore */,
                accounting.TotalDue);
            Assert.Equal(1, accounting.TxRequired);
            Assert.Equal(accounting.Paid, accounting.TotalDue);
#pragma warning restore CS0618
        }
#endif

        [Fact]
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
        public void CanAcceptInvoiceWithTolerance()
        {
            var networkProvider = CreateNetworkProvider(ChainName.Regtest);
            var entity = new InvoiceEntity();
            entity.Networks = networkProvider;
#pragma warning disable CS0618
            entity.Payments = new List<PaymentEntity>();
            entity.SetPaymentMethod(new PaymentMethod()
            {
                Currency = "BTC",
                Rate = 5000,
                NextNetworkFee = Money.Coins(0.1m)
            });
            entity.Price = 5000;
            entity.PaymentTolerance = 0;
            entity.UpdateTotals();

            var paymentMethod = entity.GetPaymentMethods().TryGet("BTC", PaymentTypes.BTCLike);
            var accounting = paymentMethod.Calculate();
            Assert.Equal(1.1m, accounting.Due);
            Assert.Equal(1.1m, accounting.TotalDue);
            Assert.Equal(1.1m, accounting.MinimumTotalDue);

            entity.PaymentTolerance = 10;
            entity.UpdateTotals();
            accounting = paymentMethod.Calculate();
            Assert.Equal(0.99m, accounting.MinimumTotalDue);

            entity.PaymentTolerance = 100;
            entity.UpdateTotals();
            accounting = paymentMethod.Calculate();
            Assert.Equal(0.0000_0001m, accounting.MinimumTotalDue);
        }

        [Fact]
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
        public void CanDetectFileType()
        {
            Assert.True(FileTypeDetector.IsPicture(new byte[] { 0x42, 0x4D }, "test.bmp"));
            Assert.False(FileTypeDetector.IsPicture(new byte[] { 0x42, 0x4D }, ".bmp"));
            Assert.False(FileTypeDetector.IsPicture(new byte[] { 0x42, 0x4D }, "test.svg"));
            Assert.True(FileTypeDetector.IsPicture(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, "test.jpg"));
            Assert.True(FileTypeDetector.IsPicture(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, "test.jpeg"));
            Assert.False(FileTypeDetector.IsPicture(new byte[] { 0xFF, 0xD8, 0xFF, 0xDA }, "test.jpg"));
            Assert.False(FileTypeDetector.IsPicture(new byte[] { 0xFF, 0xD8, 0xFF }, "test.jpg"));
            Assert.True(FileTypeDetector.IsPicture(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, "test.svg"));
            Assert.False(FileTypeDetector.IsPicture(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, "test.jpg"));
            Assert.False(FileTypeDetector.IsPicture(new byte[] { 0xFF }, "e.jpg"));
            Assert.False(FileTypeDetector.IsPicture(new byte[] { }, "empty.jpg"));

            Assert.False(FileTypeDetector.IsPicture(new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x23 }, "music.mp3"));
            Assert.True(FileTypeDetector.IsAudio(new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x23 }, "music.mp3"));
            Assert.True(FileTypeDetector.IsAudio(new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x9A, 0x08, 0x00, 0x57, 0x41 }, "music.wav"));
            Assert.True(FileTypeDetector.IsAudio(new byte[] { 0xFF, 0xF1, 0x50, 0x80, 0x1C, 0x3F, 0xFC, 0xDA, 0x00, 0x4C }, "music.aac"));
            Assert.True(FileTypeDetector.IsAudio(new byte[] { 0x66, 0x4C, 0x61, 0x43, 0x00, 0x00, 0x00, 0x22, 0x04, 0x80 }, "music.flac"));
            Assert.True(FileTypeDetector.IsAudio(new byte[] { 0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00 }, "music.ogg"));
            Assert.True(FileTypeDetector.IsAudio(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 }, "music.weba"));
            Assert.True(FileTypeDetector.IsAudio(new byte[] { 0xFF, 0xF3, 0xE4, 0x64, 0x00, 0x20, 0xAD, 0xBD, 0x04, 0x00 }, "music.mp3"));
        }

        [Fact]
        public void RoundupCurrenciesCorrectly()
        {
            DisplayFormatter displayFormatter = new(CurrencyNameTable.Instance);
            foreach (var test in new[]
            {
                (0.0005m, "0.0005 USD", "USD"), (0.001m, "0.001 USD", "USD"), (0.01m, "0.01 USD", "USD"),
                (0.1m, "0.10 USD", "USD"), (0.1m, "0,10 EUR", "EUR"), (1000m, "1,000 JPY", "JPY"),
                (1000.0001m, "1,000.00 INR", "INR"),
                (0.0m, "0.00 USD", "USD"), (1m, "1 COP", "COP"), (1m, "1 ARS", "ARS")
            })
            {
                var actual = displayFormatter.Currency(test.Item1, test.Item3);
                actual = actual.Replace("￥", "¥"); // Hack so JPY test pass on linux as well
                Assert.Equal(test.Item2, actual);
            }
            Assert.Equal(0, CurrencyNameTable.Instance.GetNumberFormatInfo("ARS").CurrencyDecimalDigits);
            Assert.Equal(0, CurrencyNameTable.Instance.GetNumberFormatInfo("COP").CurrencyDecimalDigits);
        }

        [Fact]
        public async Task CanEnumerateTorServices()
        {
            var tor = new TorServices(CreateNetworkProvider(ChainName.Regtest),
                new OptionsWrapper<BTCPayServerOptions>(new BTCPayServerOptions()
                {
                    TorrcFile = TestUtils.GetTestDataFullPath("Tor/torrc")
                }), BTCPayLogs);
            await tor.Refresh();

            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.BTCPayServer));
            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.P2P));
            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.RPC));
            Assert.True(tor.Services.Count(t => t.ServiceType == TorServiceType.Other) > 1);

            tor = new TorServices(CreateNetworkProvider(ChainName.Regtest),
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
        public void CanParseDerivationSchemes()
        {
            var networkProvider = CreateNetworkProvider(ChainName.Regtest);
            var parser = new DerivationSchemeParser(networkProvider.BTC);

            // xpub
            var xpub = "xpub661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw";
            DerivationStrategyBase strategyBase = parser.Parse(xpub);
            Assert.IsType<DirectDerivationStrategy>(strategyBase);
            Assert.True(((DirectDerivationStrategy)strategyBase).Segwit);
            Assert.Equal("tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS", strategyBase.ToString());

            // Multisig
            var multisig = "wsh(sortedmulti(2,[62a7956f/84'/1'/0']tpubDDXgATYzdQkHHhZZCMcNJj8BGDENvzMVou5v9NdxiP4rxDLj33nS233dGFW4htpVZSJ6zds9eVqAV9RyRHHiKtwQKX8eR4n4KN3Dwmj7A3h/0/*,[11312aa2/84'/1'/0']tpubDC8a54NFtQtMQAZ97VhoU9V6jVTvi9w4Y5SaAXJSBYETKg3AoX5CCKndznhPWxJUBToPCpT44s86QbKdGpKAnSjcMTGW4kE6UQ8vpBjcybW/0/*,[8f71b834/84'/1'/0']tpubDChjnP9LXNrJp43biqjY7FH93wgRRNrNxB4Q8pH7PPRy8UPcH2S6V46WGVJ47zVGF7SyBJNCpnaogsFbsybVQckGtVhCkng3EtFn8qmxptS/0/*))";
            var expected = "2-of-tpubDDXgATYzdQkHHhZZCMcNJj8BGDENvzMVou5v9NdxiP4rxDLj33nS233dGFW4htpVZSJ6zds9eVqAV9RyRHHiKtwQKX8eR4n4KN3Dwmj7A3h-tpubDC8a54NFtQtMQAZ97VhoU9V6jVTvi9w4Y5SaAXJSBYETKg3AoX5CCKndznhPWxJUBToPCpT44s86QbKdGpKAnSjcMTGW4kE6UQ8vpBjcybW-tpubDChjnP9LXNrJp43biqjY7FH93wgRRNrNxB4Q8pH7PPRy8UPcH2S6V46WGVJ47zVGF7SyBJNCpnaogsFbsybVQckGtVhCkng3EtFn8qmxptS";
            (strategyBase, RootedKeyPath[] rootedKeyPath) = parser.ParseOutputDescriptor(multisig);
            Assert.Equal(3, rootedKeyPath.Length);
            Assert.IsType<P2WSHDerivationStrategy>(strategyBase);
            Assert.IsType<MultisigDerivationStrategy>(((P2WSHDerivationStrategy)strategyBase).Inner);
            Assert.Equal(expected, strategyBase.ToString());

            var inner = (MultisigDerivationStrategy)((P2WSHDerivationStrategy)strategyBase).Inner;
            Assert.False(inner.IsLegacy);
            Assert.Equal(3, inner.Keys.Count);
            Assert.Equal(2, inner.RequiredSignatures);
            Assert.Equal(expected, inner.ToString());

            // Output Descriptor
            networkProvider = CreateNetworkProvider(ChainName.Mainnet);
            parser = new DerivationSchemeParser(networkProvider.BTC);
            var od = "wpkh([8bafd160/49h/0h/0h]xpub661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw/0/*)#9x4vkw48";
            (strategyBase, rootedKeyPath) = parser.ParseOutputDescriptor(od);
            Assert.Single(rootedKeyPath);
            Assert.IsType<DirectDerivationStrategy>(strategyBase);
            Assert.True(((DirectDerivationStrategy)strategyBase).Segwit);

            // Failure cases
            Assert.Throws<FormatException>(() => { parser.Parse("xpub 661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw"); }); // invalid format because of space
            Assert.Throws<ParsingException>(() => { parser.ParseOutputDescriptor("invalid"); }); // invalid in general
            Assert.Throws<ParsingException>(() => { parser.ParseOutputDescriptor("wpkh([8b60afd1/49h/0h/0h]xpub661MyMwAFXkMnyoBjyHndD3QwRbcGVBsTGeNZN6QGVHcfz4MPzBUxjSevweNFQx7SqmMHLdSA4FteGsRrEriu4pnVZMZWnruFFAYZATtcDw/0/*)#9x4vkw48"); }); // invalid checksum
        }

        [Fact]
        public void ParseTradeQuantity()
        {
            Assert.Throws<FormatException>(() => TradeQuantity.Parse("1.2345o"));
            Assert.Throws<FormatException>(() => TradeQuantity.Parse("o"));
            Assert.Throws<FormatException>(() => TradeQuantity.Parse(""));
            Assert.Throws<FormatException>(() => TradeQuantity.Parse("1.353%%"));
            Assert.Throws<FormatException>(() => TradeQuantity.Parse("1.353 %%"));
            Assert.Throws<FormatException>(() => TradeQuantity.Parse("-1.353%"));
            Assert.Throws<FormatException>(() => TradeQuantity.Parse("-1.353"));

            var qty = TradeQuantity.Parse("1.3%");
            Assert.Equal(1.3m, qty.Value);
            Assert.Equal(TradeQuantity.ValueType.Percent, qty.Type);
            var qty2 = TradeQuantity.Parse("1.3");
            Assert.Equal(1.3m, qty2.Value);
            Assert.Equal(TradeQuantity.ValueType.Exact, qty2.Type);
            Assert.NotEqual(qty, qty2);
            Assert.Equal(qty, TradeQuantity.Parse("1.3%"));
            Assert.Equal(qty2, TradeQuantity.Parse("1.3"));
            Assert.Equal(TradeQuantity.Parse(qty.ToString()), TradeQuantity.Parse("1.3%"));
            Assert.Equal(TradeQuantity.Parse(qty2.ToString()), TradeQuantity.Parse("1.3"));
            Assert.Equal(TradeQuantity.Parse(qty2.ToString()), TradeQuantity.Parse(" 1.3 "));
        }


        public static WalletFileParsers GetParsers()
        {
            var service = new ServiceCollection();
            BTCPayServerServices.AddOnchainWalletParsers(service);
            return service.BuildServiceProvider().GetRequiredService<WalletFileParsers>();
        }

        [Fact]
        public void ParseDerivationSchemeSettings()
        {
            var testnet = CreateNetworkProvider(ChainName.Testnet).GetNetwork<BTCPayNetwork>("BTC");
            var mainnet = CreateNetworkProvider(ChainName.Mainnet).GetNetwork<BTCPayNetwork>("BTC");
            var root = new Mnemonic(
                    "usage fever hen zero slide mammal silent heavy donate budget pulse say brain thank sausage brand craft about save attract muffin advance illegal cabbage")
                .DeriveExtKey();
            var parsers = GetParsers();
            // xpub
            var tpub = "tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS";
            Assert.True(parsers.TryParseWalletFile(tpub, testnet, out var settings, out var error));
            Assert.True(settings.AccountDerivation is DirectDerivationStrategy { Segwit: false });
            Assert.Equal($"{tpub}-[legacy]", ((DirectDerivationStrategy)settings.AccountDerivation).ToString());
            Assert.Equal("GenericFile", settings.Source);
            Assert.Null(error);

            // xpub with fingerprint and account
            tpub = "tpubDCXK98mNrPWuoWweaoUkqwxQF5NMWpQLy7n7XJgDCpwYfoZRXGafPaVM7mYqD7UKhsbMxkN864JY2PniMkt1Uk4dNuAMnWFVqdquyvZNyca";
            var vpub = "vpub5YVA1ZbrqkUVq8NZTtvRDrS2a1yoeBvHbG9NbxqJ6uRtpKGFwjQT11WEqKYsgoDF6gpqrDf8ddmPZe4yXWCjzqF8ad2Cw9xHiE8DSi3X3ik";
            var fingerprint = "e5746fd9";
            var account = "84'/1'/0'";
            var str = $"[{fingerprint}/{account}]{vpub}";
            Assert.True(parsers.TryParseWalletFile(str, testnet, out settings, out error));
            Assert.Null(error);
            Assert.True(settings.AccountDerivation is DirectDerivationStrategy { Segwit: true });
            Assert.Equal(vpub, settings.AccountOriginal);
            Assert.Equal(tpub, ((DirectDerivationStrategy)settings.AccountDerivation).ToString());
            Assert.Equal(HDFingerprint.TryParse(fingerprint, out var hd) ? hd : default, settings.AccountKeySettings[0].RootFingerprint);
            Assert.Equal(account, settings.AccountKeySettings[0].AccountKeyPath.ToString());
            Assert.Equal("GenericFile", settings.Source);
            Assert.Null(error);

            // ColdCard
            Assert.True(parsers.TryParseWalletFile(
                "{\"keystore\": {\"ckcc_xpub\": \"xpub661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw\", \"xpub\": \"ypub6WWc2gWwHbdnAAyJDnR4SPL1phRh7REqrPBfZeizaQ1EmTshieRXJC3Z5YoU4wkcdKHEjQGkh6AYEzCQC1Kz3DNaWSwdc1pc8416hAjzqyD\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/0'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}",
                mainnet, out settings, out error));
            Assert.Null(error);
            Assert.Equal(root.GetPublicKey().GetHDFingerPrint(), settings.AccountKeySettings[0].RootFingerprint);
            Assert.Equal(settings.AccountKeySettings[0].RootFingerprint,
                HDFingerprint.TryParse("8bafd160", out hd) ? hd : default);
            Assert.Equal("Coldcard Import 0x60d1af8b", settings.Label);
            Assert.Equal("49'/0'/0'", settings.AccountKeySettings[0].AccountKeyPath.ToString());
            Assert.Equal(
                "ypub6WWc2gWwHbdnAAyJDnR4SPL1phRh7REqrPBfZeizaQ1EmTshieRXJC3Z5YoU4wkcdKHEjQGkh6AYEzCQC1Kz3DNaWSwdc1pc8416hAjzqyD",
                settings.AccountOriginal);
            Assert.Equal(root.Derive(new KeyPath("m/49'/0'/0'")).Neuter().PubKey.WitHash.ScriptPubKey.Hash.ScriptPubKey,
                settings.AccountDerivation.GetDerivation().ScriptPubKey);
            Assert.Equal("ElectrumFile", settings.Source);
            Assert.Null(error);

            // Should be legacy
            Assert.True(parsers.TryParseWalletFile(
                "{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"tpubDDWYqT3P24znfsaGX7kZcQhNc5LAjnQiKQvUCHF2jS6dsgJBRtymopEU5uGpMaR5YChjuiExZG1X2aTbqXkp82KqH5qnqwWHp6EWis9ZvKr\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/44'/1'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}",
                testnet, out settings, out error));
            Assert.True(settings.AccountDerivation is DirectDerivationStrategy { Segwit: false });
            Assert.Equal("ElectrumFile", settings.Source);
            Assert.Null(error);

            // Should be segwit p2sh
            Assert.True(parsers.TryParseWalletFile(
                "{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"upub5DSddA9NoRUyJrQ4p86nsCiTSY7kLHrSxx3joEJXjHd4HPARhdXUATuk585FdWPVC2GdjsMePHb6BMDmf7c6KG4K4RPX6LVqBLtDcWpQJmh\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/49'/1'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}",
                testnet, out settings, out error));
            Assert.True(settings.AccountDerivation is P2SHDerivationStrategy { Inner: DirectDerivationStrategy { Segwit: true } });
            Assert.Equal("ElectrumFile", settings.Source);
            Assert.Null(error);

            // Should be segwit
            Assert.True(parsers.TryParseWalletFile(
                "{\"keystore\": {\"ckcc_xpub\": \"tpubD6NzVbkrYhZ4YHNiuTdTmHRmbcPRLfqgyneZFCL1mkzkUBjXriQShxTh9HL34FK2mhieasJVk9EzJrUfkFqRNQBjiXgx3n5BhPkxKBoFmaS\", \"xpub\": \"vpub5YjYxTemJ39tFRnuAhwduyxG2tKGjoEpmvqVQRPqdYrqa6YGoeSzBtHXaJUYB19zDbXs3JjbEcVWERjQBPf9bEfUUMZNMv1QnMyHV8JPqyf\", \"label\": \"Coldcard Import 0x60d1af8b\", \"ckcc_xfp\": 1624354699, \"type\": \"hardware\", \"hw_type\": \"coldcard\", \"derivation\": \"m/84'/1'/0'\"}, \"wallet_type\": \"standard\", \"use_encryption\": false, \"seed_version\": 17}",
                testnet, out settings, out error));
            Assert.True(settings.AccountDerivation is DirectDerivationStrategy { Segwit: true });
            Assert.Equal("ElectrumFile", settings.Source);
            Assert.Null(error);

            // Specter
            Assert.True(parsers.TryParseWalletFile(
                "{\"label\": \"Specter\", \"blockheight\": 123456, \"descriptor\": \"wpkh([8bafd160/49h/0h/0h]xpub661MyMwAqRbcGVBsTGeNZN6QGVHmMHLdSA4FteGsRrEriu4pnVZMZWnruFFFXkMnyoBjyHndD3Qwcfz4MPzBUxjSevweNFQx7SAYZATtcDw/0/*)#9x4vkw48\"}",
                mainnet, out var specter, out error));
            Assert.Equal(root.GetPublicKey().GetHDFingerPrint(), specter.AccountKeySettings[0].RootFingerprint);
            Assert.Equal(specter.AccountKeySettings[0].RootFingerprint, hd);
            Assert.Equal("49'/0'/0'", specter.AccountKeySettings[0].AccountKeyPath.ToString());
            Assert.True(specter.AccountDerivation is DirectDerivationStrategy { Segwit: true });
            Assert.Equal("Specter", specter.Label);
            Assert.Null(error);
            
            // Wasabi
            var wasabiJson = @"{""EncryptedSecret"": ""6PYNUAZZLS1ShkhHhm9ayiNwXPAPLN669fN5mY2WbGm1Hqc88tomqWXabU"",""ChainCode"": ""UoHIB+2mDbZSowo11TfDQbsYK6q1DrZ2H2yqQBxu6m8="",""MasterFingerprint"": ""0f215605"",""ExtPubKey"": ""xpub6DUXFa6fMrFpg7x4nEd8jBU6xDN3vkSXsVUrSbUB2dadbYaPE31czwVdv146JRStGsc2U6TywdKnGoVcP8Rtp2AZQyzXxQb7HrgmR9LrqLA"",""TaprootExtPubKey"": ""xpub6D2thLU5KwUk3axkJu1UT3yKFshCGU7TMuxhPgZMd91VvrcDwHdRwdzLk61cSHtZC6BkaipPgfFwjoDBY4m1WxyznxZLukYgM4dC6iRJVf8"",""SkipSynchronization"": true,""UseTurboSync"": true,""MinGapLimit"": 21,""AccountKeyPath"": ""84'/0'/0'"",""TaprootAccountKeyPath"": ""86'/0'/0'"",""BlockchainState"": {""Network"": ""Main"",""Height"": ""503723"",""TurboSyncHeight"": ""503723""},""PreferPsbtWorkflow"": false,""AutoCoinJoin"": true,""PlebStopThreshold"": ""0.01"",""AnonScoreTarget"": 5,""FeeRateMedianTimeFrameHours"": 0,""IsCoinjoinProfileSelected"": true,""RedCoinIsolation"": false,""ExcludedCoinsFromCoinJoin"": [],""HdPubKeys"": [{""PubKey"": ""03f88b9c3e16e40a5a9eaf8b36b9bcee7bbc93fd9eea640b541efb931ac55f7ff5"",""FullKeyPath"": ""84'/0'/0'/1/0"",""Label"": """",""KeyState"": 0},{""PubKey"": ""03e5241fc28aa556d7cb826b9a9f5ecee85287e7476746126263574a5e27fbf569"",""FullKeyPath"": ""84'/0'/0'/0/0"",""Label"": """",""KeyState"": 0}]}";
            Assert.True(parsers.TryParseWalletFile(wasabiJson, mainnet, out var wasabi, out error));
            Assert.Null(error);
            Assert.Equal("WasabiFile", wasabi.Source);
            Assert.Single(wasabi.AccountKeySettings);
            Assert.Equal("84'/0'/0'", wasabi.AccountKeySettings[0].AccountKeyPath.ToString());
            Assert.Equal("0f215605", wasabi.AccountKeySettings[0].RootFingerprint.ToString());
            Assert.True(wasabi.AccountDerivation is DirectDerivationStrategy { Segwit: true });
            
            // BSMS BIP129, Nunchuk
            var bsms = @"BSMS 1.0
wsh(sortedmulti(1,[5c9e228d/48'/0'/0'/2']xpub6EgGHjcvovyN3nK921zAGPfuB41cJXkYRdt3tLGmiMyvbgHpss4X1eRZwShbEBb1znz2e2bCkCED87QZpin3sSYKbmCzQ9Sc7LaV98ngdeX/**,[2b0e251e/48'/0'/0'/2']xpub6DrimHB8KUSkPvmJ8Pk8RE769EdDm2VEoZ8MBz76w9QupP8Py4wexs4Pa3aRB1LUEhc9GyY6ypDWEFFRCgqeDQePcyWQfjtmintrehq3JCL/**))
/0/*,/1/*
bc1qfzu57kgu5jthl934f9xrdzzx8mmemx7gn07tf0grnvz504j6kzusu2v0ku
";

            Assert.True(parsers.TryParseWalletFile(bsms,
                mainnet, out var nunchuk, out error));

            Assert.Equal(2, nunchuk.AccountKeySettings.Length);
            //check that the account key settings match those in bsms string
            Assert.Equal("5c9e228d", nunchuk.AccountKeySettings[0].RootFingerprint.ToString());
            Assert.Equal("48'/0'/0'/2'", nunchuk.AccountKeySettings[0].AccountKeyPath.ToString());
            Assert.Equal("2b0e251e", nunchuk.AccountKeySettings[1].RootFingerprint.ToString());
            Assert.Equal("48'/0'/0'/2'", nunchuk.AccountKeySettings[1].AccountKeyPath.ToString());

            var multsig = Assert.IsType<MultisigDerivationStrategy>
                          (Assert.IsType<P2WSHDerivationStrategy>(nunchuk.AccountDerivation).Inner);

            Assert.True(multsig.LexicographicOrder);
            Assert.Equal(1, multsig.RequiredSignatures);

            var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);
            var line = nunchuk.AccountDerivation.GetLineFor(deposit).Derive(0);

            Assert.Equal(BitcoinAddress.Create("bc1qfzu57kgu5jthl934f9xrdzzx8mmemx7gn07tf0grnvz504j6kzusu2v0ku", Network.Main).ScriptPubKey,
                line.ScriptPubKey);

            Assert.Equal("BSMS", nunchuk.Source);
            Assert.Null(error);


            // Failure case
            Assert.False(parsers.TryParseWalletFile(
                "{\"keystore\": {\"ckcc_xpub\": \"tpubFailure\", \"xpub\": \"tpubFailure\", \"label\": \"Failure\"}, \"wallet_type\": \"standard\"}",
                testnet, out settings, out error));
            Assert.Null(settings);
            Assert.NotNull(error);


            //passport 
            var passportText =
                "{\"Source\": \"Passport\", \"Descriptor\": \"tr([5c9e228d/86'/0'/0']xpub6EgGHjcvovyN3nK921zAGPfuB41cJXkYRdt3tLGmiMyvbgHpss4X1eRZwShbEBb1znz2e2bCkCED87QZpin3sSYKbmCzQ9Sc7LaV98ngdeX/0/*)\", \"FirmwareVersion\": \"v1.0.0\"}";
            Assert.True(parsers.TryParseWalletFile(passportText, mainnet, out var passport, out error));
            Assert.Equal("Passport", passport.Source);
            Assert.True(passport.AccountDerivation is TaprootDerivationStrategy);
            Assert.Equal("5c9e228d", passport.AccountKeySettings[0].RootFingerprint.ToString());
            Assert.Equal("86'/0'/0'", passport.AccountKeySettings[0].AccountKeyPath.ToString());

            //electrum
            var electrumText =
"""
{
  "keystore": {
    "xpub": "vpub5Z14bnDNoEQeFdwZYSpVHcpzRpH99CnvSemzqTAvhjcgBTzPUVnaA5GhjgZc9J46duUprxQRUVUuqchazanXD6bLuVyarviNHBFUu6fBZNj",
    "xprv": "vprv9ENJcv8RKwqMTqyhLSuBz5bEV7hpdZjisjUBuV9K8azz1vpop6xJFEDRdfDwgWBpYgUUhEVxdvpxgV3f8NircysfebnBaPu5y2dcnSDAEEw",
    "type": "bip32",
    "pw_hash_version": 1
  },
  "wallet_type": "standard",
  "use_encryption": false,
  "seed_type": "bip39"
}
""";
            Assert.True(parsers.TryParseWalletFile(electrumText, testnet, out var electrum, out _));
            Assert.Equal("ElectrumFile", electrum.Source);

            electrumText =
"""
{
"keystore": {
    "derivation": "m/0h",
    "pw_hash_version": 1,
    "root_fingerprint": "fbb5b37d",
    "seed": "tiger room acoustic bracket thing film umbrella rather pepper tired vault remain",
    "seed_type": "segwit",
    "type": "bip32",
    "xprv": "zprvAaQyp6mTAX53zY4j2BbecRNtmTq2kSEKgy2y4yK3bFPKgPJLxrMmPxzZdRkWq5XvmtH2R4ko5YmJYH2MgnVkWr32pHi4Dc5627WyML32KTW",
    "xpub": "zpub6oQLDcJLztdMD29C8D8eyZKdKVfX9txB4BxZsMif9avJZBdVWPg1wmK3Uh3VxU7KXon1wm1xzvjyqmKWguYMqyjKP5f5Cho9f7uLfmRt2Br"
},
"wallet_type": "standard",
"use_encryption": false,
"seed_type": "bip39"
}
""";
            Assert.True(parsers.TryParseWalletFile(electrumText, mainnet, out electrum, out _));
            Assert.Equal("ElectrumFile", electrum.Source);
            Assert.Equal("0'", electrum.GetSigningAccountKeySettings().AccountKeyPath.ToString());
            Assert.True(electrum.AccountDerivation is DirectDerivationStrategy { Segwit: true });
            Assert.Equal("fbb5b37d", electrum.GetSigningAccountKeySettings().RootFingerprint.ToString());
            Assert.Equal("zpub6oQLDcJLztdMD29C8D8eyZKdKVfX9txB4BxZsMif9avJZBdVWPg1wmK3Uh3VxU7KXon1wm1xzvjyqmKWguYMqyjKP5f5Cho9f7uLfmRt2Br", electrum.AccountOriginal);
            Assert.Equal(((DirectDerivationStrategy)electrum.AccountDerivation).GetExtPubKeys().First().ParentFingerprint.ToString(), electrum.GetSigningAccountKeySettings().RootFingerprint.ToString());

            // Electrum with strange garbage at the end caused by the lightning support
            electrumText =
"""
{
"keystore": {
    "derivation": "m/0h",
    "pw_hash_version": 1,
    "root_fingerprint": "fbb5b37d",
    "seed": "tiger room acoustic bracket thing film umbrella rather pepper tired vault remain",
    "seed_type": "segwit",
    "type": "bip32",
    "xprv": "zprvAaQyp6mTAX53zY4j2BbecRNtmTq2kSEKgy2y4yK3bFPKgPJLxrMmPxzZdRkWq5XvmtH2R4ko5YmJYH2MgnVkWr32pHi4Dc5627WyML32KTW",
    "xpub": "zpub6oQLDcJLztdMD29C8D8eyZKdKVfX9txB4BxZsMif9avJZBdVWPg1wmK3Uh3VxU7KXon1wm1xzvjyqmKWguYMqyjKP5f5Cho9f7uLfmRt2Br"
},
"wallet_type": "standard",
"use_encryption": false,
"seed_type": "bip39"
},
{"op": "remove", "path": "/channels"}
""";
            Assert.True(parsers.TryParseWalletFile(electrumText, mainnet, out electrum, out _));
        }

        [Fact]
        public async Task CheckRatesProvider()
        {
            var spy = new SpyRateProvider();
            RateRules.TryParse("X_X = bitpay(X_X);", out var rateRules);

            var factory = CreateBTCPayRateFactory();
            factory.Providers.Clear();
            var fetcher = new RateFetcher(factory);
            factory.Providers.Clear();
            var fetch = new BackgroundFetcherRateProvider(spy);
            fetch.DoNotAutoFetchIfExpired = true;
            factory.Providers.Add("bitpay", fetch);
            var fetchedRate = await fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules, default);
            spy.AssertHit();
            fetchedRate = await fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules, default);
            spy.AssertNotHit();
            await fetch.UpdateIfNecessary(default);
            spy.AssertNotHit();
            fetch.RefreshRate = TimeSpan.FromSeconds(1.0);
            Thread.Sleep(1020);
            fetchedRate = await fetcher.FetchRate(CurrencyPair.Parse("BTC_USD"), rateRules, default);
            spy.AssertNotHit();
            fetch.ValidatyTime = TimeSpan.FromSeconds(1.0);
            await fetch.UpdateIfNecessary(default);
            spy.AssertHit();
            await fetch.GetRatesAsync(default);
            Thread.Sleep(1000);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fetch.GetRatesAsync(default));
        }

        public static RateProviderFactory CreateBTCPayRateFactory()
        {
            ServiceCollection services = new ServiceCollection();
            services.AddHttpClient();
            BTCPayServerServices.RegisterRateSources(services);
            var o = services.BuildServiceProvider();
            return new RateProviderFactory(TestUtils.CreateHttpFactory(), o.GetService<IEnumerable<IRateProvider>>());
        }

        class SpyRateProvider : IRateProvider
        {
            public bool Hit { get; set; }

            public RateSourceInfo RateSourceInfo => new("spy", "SPY", "https://spy.org");

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

        [Fact]
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
        public void CanParseFilter()
        {
            var storeId = "6DehZnc9S7qC6TUTNWuzJ1pFsHTHvES6An21r3MjvLey";
            var filter = "storeid:abc, status:abed, blabhbalh ";
            var search = new SearchString(filter);
            Assert.Equal("storeid:abc, status:abed, blabhbalh", search.ToString());
            Assert.Equal("blabhbalh", search.TextSearch);
            Assert.Single(search.Filters["storeid"], "abc");
            Assert.Single(search.Filters["status"], "abed");

            filter = "status:abed, status:abed2";
            search = new SearchString(filter);
            Assert.Null(search.TextSearch);
            Assert.Null(search.TextFilters);
            Assert.Equal("status:abed, status:abed2", search.ToString());
            Assert.Throws<KeyNotFoundException>(() => search.Filters["test"]);
            Assert.Equal(2, search.Filters["status"].Count);
            Assert.Equal("abed", search.Filters["status"].First());
            Assert.Equal("abed2", search.Filters["status"].Skip(1).First());

            filter = "StartDate:2019-04-25 01:00 AM, hekki,orderid:MYORDERID,orderid:MYORDERID_2";
            search = new SearchString(filter);
            Assert.Equal("2019-04-25 01:00 AM", search.Filters["startdate"].First());
            Assert.Equal("hekki", search.TextSearch);
            Assert.Equal("orderid:MYORDERID,orderid:MYORDERID_2", search.TextFilters);
            Assert.Equal("orderid:MYORDERID,orderid:MYORDERID_2,hekki", search.TextCombined);
            Assert.Equal("StartDate:2019-04-25 01:00 AM", search.WithoutSearchText());
            Assert.Equal(filter, search.ToString());

            // modify search
            filter = $"status:settled,exceptionstatus:paidLate,unusual:true, fulltext searchterm, storeid:{storeId},startdate:2019-04-25 01:00:00";
            search = new SearchString(filter);
            Assert.Equal(filter, search.ToString());
            Assert.Equal("fulltext searchterm", search.TextSearch);
            Assert.Single(search.Filters["storeid"], storeId);
            Assert.Single(search.Filters["status"], "settled");
            Assert.Single(search.Filters["exceptionstatus"], "paidLate");
            Assert.Single(search.Filters["unusual"], "true");

            // toggle off bool with same value
            var modified = new SearchString(search.Toggle("unusual", "true"));
            Assert.Null(modified.GetFilterBool("unusual"));

            // add to array
            modified = new SearchString(modified.Toggle("status", "processing"));
            var statusArray = modified.GetFilterArray("status");
            Assert.Equal(2, statusArray.Length);
            Assert.Contains("processing", statusArray);
            Assert.Contains("settled", statusArray);

            // toggle off array with same value
            modified = new SearchString(modified.Toggle("status", "settled"));
            statusArray = modified.GetFilterArray("status");
            Assert.Single(statusArray, "processing");

            // toggle off array with null value
            modified = new SearchString(modified.Toggle("status", null));
            Assert.Null(modified.GetFilterArray("status"));

            // toggle off date with null value
            modified = new SearchString(modified.Toggle("startdate", "-7d"));
            Assert.Single(modified.GetFilterArray("startdate"), "-7d");
            modified = new SearchString(modified.Toggle("startdate", null));
            Assert.Null(modified.GetFilterArray("startdate"));

            // toggle off date with same value
            modified = new SearchString(modified.Toggle("enddate", "-7d"));
            Assert.Single(modified.GetFilterArray("enddate"), "-7d");
            modified = new SearchString(modified.Toggle("enddate", "-7d"));
            Assert.Null(modified.GetFilterArray("enddate"));
        }

        [Fact]
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
        public void HasCurrencyDataForNetworks()
        {
            var btcPayNetworkProvider = CreateNetworkProvider(ChainName.Regtest);
            foreach (var network in btcPayNetworkProvider.GetAll())
            {
                var cd = CurrencyNameTable.Instance.GetCurrencyData(network.CryptoCode, false);
                Assert.NotNull(cd);
                Assert.Equal(network.Divisibility, cd.Divisibility);
                Assert.True(cd.Crypto);
            }
        }

        [Fact]
        public void SetOrderIdMetadataDoesntConvertInOctal()
        {
            var m = new InvoiceMetadata();
            m.OrderId = "000000161";
            Assert.Equal("000000161", m.OrderId);
        }

        [Fact]
        public void CanParseOldPosAppData()
        {
            var data = new JObject()
            {
                ["price"] = 1.64m
            }.ToString();
            Assert.Equal(1.64m, JsonConvert.DeserializeObject<PosAppCartItem>(data).Price);

            data = new JObject()
            {
                ["price"] = new JObject()
                {
                    ["value"] = 1.65m
                }
            }.ToString();
            Assert.Equal(1.65m, JsonConvert.DeserializeObject<PosAppCartItem>(data).Price);
            data = new JObject()
            {
                ["price"] = new JObject()
                {
                    ["value"] = "1.6305"
                }
            }.ToString();
            Assert.Equal(1.6305m, JsonConvert.DeserializeObject<PosAppCartItem>(data).Price);

            data = new JObject()
            {
                ["price"] = new JObject()
                {
                    ["value"] = null
                }
            }.ToString();
            Assert.Equal(0.0m, JsonConvert.DeserializeObject<PosAppCartItem>(data).Price);

            var o = JObject.Parse(JsonConvert.SerializeObject(new PosAppCartItem() { Price = 1.356m }));
            Assert.Equal(1.356m, o["price"].Value<decimal>());
        }

        [Fact]
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
        public async Task MultiProcessingQueueTests()
        {
            MultiProcessingQueue q = new MultiProcessingQueue();
            var q10 = Enqueue(q, "q1");
            var q11 = Enqueue(q, "q1");
            var q20 = Enqueue(q, "q2");
            var q30 = Enqueue(q, "q3");
            q10.AssertStarted();
            q11.AssertStopped();
            q20.AssertStarted();
            q30.AssertStarted();
            Assert.Equal(3, q.QueueCount);
            q10.Done();
            q10.AssertStopped();
            q11.AssertStarted();
            q20.AssertStarted();
            Assert.Equal(3, q.QueueCount);
            q30.Done();
            q30.AssertStopped();
            TestUtils.Eventually(() => Assert.Equal(2, q.QueueCount), 1000);
            await q.Abort(default);
            q11.AssertAborted();
            q20.AssertAborted();
            Assert.Equal(0, q.QueueCount);
        }
        class MultiProcessingQueueTest
        {
            public bool Started;
            public bool Aborted;
            public TaskCompletionSource Tcs;
            public void Done() { Tcs.TrySetResult(); }

            public void AssertStarted()
            {
                TestUtils.Eventually(() => Assert.True(Started), 1000);
            }
            public void AssertStopped()
            {
                TestUtils.Eventually(() => Assert.False(Started), 1000);
            }
            public void AssertAborted()
            {
                TestUtils.Eventually(() => Assert.True(Aborted), 1000);
            }
        }
        private static MultiProcessingQueueTest Enqueue(MultiProcessingQueue q, string queueName)
        {
            MultiProcessingQueueTest t = new MultiProcessingQueueTest();
            t.Tcs = new TaskCompletionSource();
            q.Enqueue(queueName, async (cancellationToken) =>
            {
                t.Started = true;
                try
                {
                    await t.Tcs.Task.WaitAsync(cancellationToken);
                }
                catch { t.Aborted = true; }
                t.Started = false;
            });
            return t;
        }

        [Fact]
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
        public void PosDataParser_ParsesCorrectly()
        {
            var testCases =
                new List<(string input, Dictionary<string, object> expectedOutput)>()
                {
                    {(null, new Dictionary<string, object>())},
                    {("", new Dictionary<string, object>())},
                    {("{}", new Dictionary<string, object>())},
                    {("{ \"key\": \"value\"}", new Dictionary<string, object>() {{"key", "value"}})},
                    // Duplicate keys should not crash things
                    {("{ \"key\": true, \"key\": true}", new Dictionary<string, object>() {{"key", "True"}})}
                };

            testCases.ForEach(tuple =>
            {
                Assert.Equal(tuple.expectedOutput, UIInvoiceController.PosDataParser.ParsePosData(string.IsNullOrEmpty(tuple.input) ? null : JToken.Parse(tuple.input)));
            });
        }
        [Fact]
        public void SecondDuplicatedRuleIsIgnored()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("DOGE_X = 1.1");
            builder.AppendLine("DOGE_X = 1.2");
            Assert.True(RateRules.TryParse(builder.ToString(), out var rules));
            var rule = rules.GetRuleFor(new CurrencyPair("DOGE", "BTC"));
            rule.Reevaluate();
            Assert.True(!rule.HasError);
            Assert.Equal(1.1m, rule.BidAsk.Ask);
        }

        [Fact]
        public void CanSerializeExchangeRatesCache()
        {
            HostedServices.RatesHostedService.ExchangeRatesCache cache = new HostedServices.RatesHostedService.ExchangeRatesCache();
            cache.Created = DateTimeOffset.UtcNow;
            cache.States = new List<Services.Rates.BackgroundFetcherState>();
            cache.States.Add(new Services.Rates.BackgroundFetcherState()
            {
                ExchangeName = "Kraken",
                LastRequested = DateTimeOffset.UtcNow,
                LastUpdated = DateTimeOffset.UtcNow,
                Rates = new List<Services.Rates.BackgroundFetcherRate>()
                {
                    new Services.Rates.BackgroundFetcherRate()
                    {
                        Pair = new CurrencyPair("USD", "BTC"),
                        BidAsk = new BidAsk(1.0m, 2.0m)
                    }
                }
            });
            var str = JsonConvert.SerializeObject(cache, Formatting.Indented);

            var cache2 = JsonConvert.DeserializeObject<HostedServices.RatesHostedService.ExchangeRatesCache>(str);
            Assert.Equal(cache.Created.ToUnixTimeSeconds(), cache2.Created.ToUnixTimeSeconds());
            Assert.Equal(cache.States[0].Rates[0].BidAsk, cache2.States[0].Rates[0].BidAsk);
            Assert.Equal(cache.States[0].Rates[0].Pair, cache2.States[0].Rates[0].Pair);
        }

        [Fact]
        public void CanParseStoreRoleId()
        {
            var id = StoreRoleId.Parse("test::lol");
            Assert.Equal("test", id.StoreId);
            Assert.Equal("lol", id.Role);
            Assert.Equal("test::lol", id.ToString());
            Assert.Equal("test::lol", id.Id);
            Assert.False(id.IsServerRole);

            id = StoreRoleId.Parse("lol");
            Assert.Null(id.StoreId);
            Assert.Equal("lol", id.Role);
            Assert.Equal("lol", id.ToString());
            Assert.Equal("lol", id.Id);
            Assert.True(id.IsServerRole);
        }

        [Fact]
        public void KitchenSinkTest()
        {
            var b = JsonConvert.DeserializeObject<PullPaymentBlob>("{}");
            Assert.Equal(TimeSpan.FromDays(30.0), b.BOLT11Expiration);
            JsonConvert.SerializeObject(b);
        }

        [Fact]
        public void CanParseRateRules()
        {
            var pair = CurrencyPair.Parse("USD_EMAT_IC");
            Assert.Equal("USD", pair.Left);
            Assert.Equal("EMAT_IC", pair.Right);
            // Check happy path
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("// Some cool comments");
            builder.AppendLine("DOGE_X = DOGE_BTC * BTC_X * 1.1");
            builder.AppendLine("DOGE_BTC = bitpay(DOGE_BTC)");
            builder.AppendLine("// Some other cool comments");
            builder.AppendLine("BTC_usd = kraken(BTC_USD)");
            builder.AppendLine("BTC_X = Coinbase(BTC_X);");
            builder.AppendLine("X_X = CoinAverage(X_X) * 1.02");

            Assert.False(RateRules.TryParse("DPW*&W&#hdi&#&3JJD", out var rules));
            Assert.True(RateRules.TryParse(builder.ToString(), out rules));
            Assert.Equal(
                "// Some cool comments\n" +
                "DOGE_X = DOGE_BTC * BTC_X * 1.1;\n" +
                "DOGE_BTC = bitpay(DOGE_BTC);\n" +
                "// Some other cool comments\n" +
                "BTC_USD = kraken(BTC_USD);\n" +
                "BTC_X = coinbase(BTC_X);\n" +
                "X_X = coinaverage(X_X) * 1.02;",
                rules.ToString());
            var tests = new[]
            {
                (Pair: "DOGE_USD", Expected: "bitpay(DOGE_BTC) * kraken(BTC_USD) * 1.1"),
                (Pair: "BTC_USD", Expected: "kraken(BTC_USD)"),
                (Pair: "BTC_CAD", Expected: "coinbase(BTC_CAD)"),
                (Pair: "DOGE_CAD", Expected: "bitpay(DOGE_BTC) * coinbase(BTC_CAD) * 1.1"),
                (Pair: "LTC_CAD", Expected: "coinaverage(LTC_CAD) * 1.02"),
                (Pair: "SATS_CAD", Expected: "0.00000001 * coinbase(BTC_CAD)"),
                (Pair: "Sats_USD", Expected: "0.00000001 * kraken(BTC_USD)")
            };
            foreach (var test in tests)
            {
                Assert.Equal(test.Expected, rules.GetRuleFor(CurrencyPair.Parse(test.Pair)).ToString());
            }
            rules.Spread = 0.2m;
            Assert.Equal("(bitpay(DOGE_BTC) * kraken(BTC_USD) * 1.1) * (0.8, 1.2)", rules.GetRuleFor(CurrencyPair.Parse("DOGE_USD")).ToString());
            ////////////////

            // Check errors conditions
            builder = new StringBuilder();
            builder.AppendLine("DOGE_X = LTC_CAD * BTC_X * 1.1");
            builder.AppendLine("DOGE_BTC = bitpay(DOGE_BTC)");
            builder.AppendLine("BTC_usd = kraken(BTC_USD)");
            builder.AppendLine("LTC_CHF = LTC_CHF * 1.01");
            builder.AppendLine("BTC_X = Coinbase(BTC_X)");
            Assert.True(RateRules.TryParse(builder.ToString(), out rules));

            tests = new[]
            {
                (Pair: "LTC_CAD", Expected: "ERR_NO_RULE_MATCH(LTC_CAD)"),
                (Pair: "DOGE_USD", Expected: "ERR_NO_RULE_MATCH(LTC_CAD) * kraken(BTC_USD) * 1.1"),
                (Pair: "LTC_CHF", Expected: "ERR_TOO_MUCH_NESTED_CALLS(LTC_CHF) * 1.01"),
            };
            foreach (var test in tests)
            {
                Assert.Equal(test.Expected, rules.GetRuleFor(CurrencyPair.Parse(test.Pair)).ToString());
            }
            //////////////////

            // Check if we can resolve exchange rates
            builder = new StringBuilder();
            builder.AppendLine("DOGE_X = DOGE_BTC * BTC_X * 1.1");
            builder.AppendLine("DOGE_BTC = bitpay(DOGE_BTC)");
            builder.AppendLine("BTC_usd = kraken(BTC_USD)");
            builder.AppendLine("BTC_X = Coinbase(BTC_X)");
            builder.AppendLine("X_X = CoinAverage(X_X) * 1.02");
            Assert.True(RateRules.TryParse(builder.ToString(), out rules));

            var tests2 = new[]
            {
                (Pair: "DOGE_USD", Expected: "bitpay(DOGE_BTC) * kraken(BTC_USD) * 1.1", ExpectedExchangeRates: "bitpay(DOGE_BTC),kraken(BTC_USD)"),
                (Pair: "BTC_USD", Expected: "kraken(BTC_USD)", ExpectedExchangeRates: "kraken(BTC_USD)"),
                (Pair: "BTC_CAD", Expected: "coinbase(BTC_CAD)", ExpectedExchangeRates: "coinbase(BTC_CAD)"),
                (Pair: "DOGE_CAD", Expected: "bitpay(DOGE_BTC) * coinbase(BTC_CAD) * 1.1", ExpectedExchangeRates: "bitpay(DOGE_BTC),coinbase(BTC_CAD)"),
                (Pair: "LTC_CAD", Expected: "coinaverage(LTC_CAD) * 1.02", ExpectedExchangeRates: "coinaverage(LTC_CAD)"),
                (Pair: "SATS_USD", Expected: "0.00000001 * kraken(BTC_USD)", ExpectedExchangeRates: "kraken(BTC_USD)"),
                (Pair: "SATS_EUR", Expected: "0.00000001 * coinbase(BTC_EUR)", ExpectedExchangeRates: "coinbase(BTC_EUR)")
            };
            foreach (var test in tests2)
            {
                var rule = rules.GetRuleFor(CurrencyPair.Parse(test.Pair));
                Assert.Equal(test.Expected, rule.ToString());
                Assert.Equal(test.ExpectedExchangeRates, string.Join(',', rule.ExchangeRates.OfType<object>().ToArray()));
            }
            var rule2 = rules.GetRuleFor(CurrencyPair.Parse("DOGE_CAD"));
            rule2.ExchangeRates.SetRate("bitpay", CurrencyPair.Parse("DOGE_BTC"), new BidAsk(5000m));
            rule2.Reevaluate();
            Assert.True(rule2.HasError);
            Assert.Equal("5000 * ERR_RATE_UNAVAILABLE(coinbase, BTC_CAD) * 1.1", rule2.ToString(true));
            Assert.Equal("bitpay(DOGE_BTC) * coinbase(BTC_CAD) * 1.1", rule2.ToString(false));
            rule2.ExchangeRates.SetRate("coinbase", CurrencyPair.Parse("BTC_CAD"), new BidAsk(2000.4m));
            rule2.Reevaluate();
            Assert.False(rule2.HasError);
            Assert.Equal("5000 * 2000.4 * 1.1", rule2.ToString(true));
            Assert.Equal(5000m * 2000.4m * 1.1m, rule2.BidAsk.Bid);
            ////////

            // Make sure parenthesis are correctly calculated
            builder = new StringBuilder();
            builder.AppendLine("DOGE_X = DOGE_BTC * BTC_X");
            builder.AppendLine("BTC_USD = -3 + coinbase(BTC_CAD) + 50 - 5");
            builder.AppendLine("DOGE_BTC = 2000");
            Assert.True(RateRules.TryParse(builder.ToString(), out rules));
            rules.Spread = 0.1m;

            rule2 = rules.GetRuleFor(CurrencyPair.Parse("DOGE_USD"));
            Assert.Equal("(2000 * (-3 + coinbase(BTC_CAD) + 50 - 5)) * (0.9, 1.1)", rule2.ToString());
            rule2.ExchangeRates.SetRate("coinbase", CurrencyPair.Parse("BTC_CAD"), new BidAsk(1000m));
            Assert.True(rule2.Reevaluate());
            Assert.Equal("(2000 * (-3 + 1000 + 50 - 5)) * (0.9, 1.1)", rule2.ToString(true));
            Assert.Equal((2000m * (-3m + 1000m + 50m - 5m)) * 0.9m, rule2.BidAsk.Bid);

            // Test inverse
            rule2 = rules.GetRuleFor(CurrencyPair.Parse("USD_DOGE"));
            Assert.Equal("(1 / (2000 * (-3 + coinbase(BTC_CAD) + 50 - 5))) * (0.9, 1.1)", rule2.ToString());
            rule2.ExchangeRates.SetRate("coinbase", CurrencyPair.Parse("BTC_CAD"), new BidAsk(1000m));
            Assert.True(rule2.Reevaluate());
            Assert.Equal("(1 / (2000 * (-3 + 1000 + 50 - 5))) * (0.9, 1.1)", rule2.ToString(true));
            Assert.Equal((1.0m / (2000m * (-3m + 1000m + 50m - 5m))) * 0.9m, rule2.BidAsk.Bid);
            ////////

            // Make sure kraken is not converted to CurrencyPair
            builder = new StringBuilder();
            builder.AppendLine("BTC_USD = kraken(BTC_USD)");
            Assert.True(RateRules.TryParse(builder.ToString(), out rules));
            rule2 = rules.GetRuleFor(CurrencyPair.Parse("BTC_USD"));
            rule2.ExchangeRates.SetRate("kraken", CurrencyPair.Parse("BTC_USD"), new BidAsk(1000m));
            Assert.True(rule2.Reevaluate());

            // Make sure can handle pairs
            builder = new StringBuilder();
            builder.AppendLine("BTC_USD = kraken(BTC_USD)");
            Assert.True(RateRules.TryParse(builder.ToString(), out rules));
            rule2 = rules.GetRuleFor(CurrencyPair.Parse("BTC_USD"));
            rule2.ExchangeRates.SetRate("kraken", CurrencyPair.Parse("BTC_USD"), new BidAsk(6000m, 6100m));
            Assert.True(rule2.Reevaluate());
            Assert.Equal("(6000, 6100)", rule2.ToString(true));
            Assert.Equal(6000m, rule2.BidAsk.Bid);
            rule2 = rules.GetRuleFor(CurrencyPair.Parse("USD_BTC"));
            rule2.ExchangeRates.SetRate("kraken", CurrencyPair.Parse("BTC_USD"), new BidAsk(6000m, 6100m));
            Assert.True(rule2.Reevaluate());
            Assert.Equal("1 / (6000, 6100)", rule2.ToString(true));
            Assert.Equal(1m / 6100m, rule2.BidAsk.Bid);

            // Make sure the inverse has more priority than X_X or CDNT_X
            builder = new StringBuilder();
            builder.AppendLine("EUR_CDNT = 10");
            builder.AppendLine("CDNT_BTC = CDNT_EUR * EUR_BTC;");
            builder.AppendLine("CDNT_X = CDNT_BTC * BTC_X;");
            builder.AppendLine("X_X = coinaverage(X_X);");
            Assert.True(RateRules.TryParse(builder.ToString(), out rules));
            rule2 = rules.GetRuleFor(CurrencyPair.Parse("CDNT_EUR"));
            rule2.ExchangeRates.SetRate("coinaverage", CurrencyPair.Parse("BTC_USD"), new BidAsk(6000m, 6100m));
            Assert.True(rule2.Reevaluate());
            Assert.Equal("1 / 10", rule2.ToString(false));

            // Make sure an inverse can be solved on an exchange
            builder = new StringBuilder();
            builder.AppendLine("X_X = coinaverage(X_X);");
            Assert.True(RateRules.TryParse(builder.ToString(), out rules));
            rule2 = rules.GetRuleFor(CurrencyPair.Parse("USD_BTC"));
            rule2.ExchangeRates.SetRate("coinaverage", CurrencyPair.Parse("BTC_USD"), new BidAsk(6000m, 6100m));
            Assert.True(rule2.Reevaluate());
            Assert.Equal($"({(1m / 6100m).ToString(CultureInfo.InvariantCulture)}, {(1m / 6000m).ToString(CultureInfo.InvariantCulture)})", rule2.ToString(true));

            // Make sure defining value in sats works
            builder = new StringBuilder();
            builder.AppendLine("BTC_USD = kraken(BTC_USD)");
            builder.AppendLine("BTC_X = coinbase(BTC_X)");
            Assert.True(RateRules.TryParse(builder.ToString(), out rules));
            rule2 = rules.GetRuleFor(CurrencyPair.Parse("SATS_USD"));
            rule2.ExchangeRates.SetRate("kraken", CurrencyPair.Parse("BTC_USD"), new BidAsk(6000m, 6100m));
            Assert.True(rule2.Reevaluate());
            Assert.Equal("0.00000001 * (6000, 6100)", rule2.ToString(true));
            Assert.Equal(0.00006m, rule2.BidAsk.Bid);
            rule2 = rules.GetRuleFor(CurrencyPair.Parse("USD_SATS"));
            rule2.ExchangeRates.SetRate("kraken", CurrencyPair.Parse("BTC_USD"), new BidAsk(6000m, 6100m));
            Assert.True(rule2.Reevaluate());
            Assert.Equal("1 / (0.00000001 * (6000, 6100))", rule2.ToString(true));
            Assert.Equal(1m / 0.000061m, rule2.BidAsk.Bid);

            // testing rounding 
            rule2 = rules.GetRuleFor(CurrencyPair.Parse("SATS_EUR"));
            rule2.ExchangeRates.SetRate("coinbase", CurrencyPair.Parse("BTC_EUR"), new BidAsk(1.23m, 2.34m));
            Assert.True(rule2.Reevaluate());
            Assert.Equal("0.00000001 * (1.23, 2.34)", rule2.ToString(true));
            Assert.Equal(0.0000000234m, rule2.BidAsk.Ask);
            Assert.Equal(0.0000000123m, rule2.BidAsk.Bid);

            rule2 = rules.GetRuleFor(CurrencyPair.Parse("EUR_SATS"));
            rule2.ExchangeRates.SetRate("coinbase", CurrencyPair.Parse("BTC_EUR"), new BidAsk(1.23m, 2.34m));
            Assert.True(rule2.Reevaluate());
            Assert.Equal("1 / (0.00000001 * (1.23, 2.34))", rule2.ToString(true));
            Assert.Equal(1m / 0.0000000123m, rule2.BidAsk.Ask);
            Assert.Equal(1m / 0.0000000234m, rule2.BidAsk.Bid);
        }

        [Theory()]
        [InlineData("DE-de")]
        [InlineData("")]
        public void NumericJsonConverterTests(string culture)
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
            JsonReader Get(string val)
            {
                return new JsonTextReader(new StringReader(val));
            }

            var jsonConverter = new NumericStringJsonConverter();
            Assert.True(jsonConverter.CanConvert(typeof(decimal)));
            Assert.True(jsonConverter.CanConvert(typeof(decimal?)));
            Assert.True(jsonConverter.CanConvert(typeof(double)));
            Assert.True(jsonConverter.CanConvert(typeof(double?)));
            Assert.False(jsonConverter.CanConvert(typeof(float)));
            Assert.False(jsonConverter.CanConvert(typeof(string)));

            var numberJson = "1";
            var numberDecimalJson = "1.2";
            var stringJson = "\"1.2\"";
            Assert.Equal(1m, jsonConverter.ReadJson(Get(numberJson), typeof(decimal), null, null));
            Assert.Equal(1.2m, jsonConverter.ReadJson(Get(numberDecimalJson), typeof(decimal), null, null));
            Assert.Null(jsonConverter.ReadJson(Get("null"), typeof(decimal?), null, null));
            Assert.Equal((double)1.0, jsonConverter.ReadJson(Get(numberJson), typeof(double), null, null));
            Assert.Equal((double)1.2, jsonConverter.ReadJson(Get(numberDecimalJson), typeof(double), null, null));
            Assert.Null(jsonConverter.ReadJson(Get("null"), typeof(double?), null, null));
            Assert.Throws<JsonSerializationException>(() =>
            {
                jsonConverter.ReadJson(Get("null"), typeof(decimal), null, null);
            });
            Assert.Throws<JsonSerializationException>(() =>
            {
                jsonConverter.ReadJson(Get("null"), typeof(double), null, null);
            });
            Assert.Equal(1.2m, jsonConverter.ReadJson(Get(stringJson), typeof(decimal), null, null));
            Assert.Equal(1.2m, jsonConverter.ReadJson(Get(stringJson), typeof(decimal?), null, null));
            Assert.Equal(1.2, jsonConverter.ReadJson(Get(stringJson), typeof(double), null, null));
            Assert.Equal(1.2, jsonConverter.ReadJson(Get(stringJson), typeof(double?), null, null));
        }

        [Fact]
        [Trait("Altcoins", "Altcoins")]
        public void LoadSubChainsAlways()
        {
            var config = new ConfigurationRoot(new List<IConfigurationProvider>()
            {
                new MemoryConfigurationProvider(new MemoryConfigurationSource()
                {
                    InitialData = new[] {
                        new KeyValuePair<string, string>("chains", "usdt")}
                })
            });
            var networkProvider = CreateNetworkProvider(config);
            Assert.NotNull(networkProvider.GetNetwork("LBTC"));
            Assert.NotNull(networkProvider.GetNetwork("USDT"));
        }
        [Fact]
        [Trait("Altcoins", "Altcoins")]
        public void CanParseDerivationScheme()
        {
            var testnetNetworkProvider = CreateNetworkProvider(ChainName.Testnet);
            var regtestNetworkProvider = CreateNetworkProvider(ChainName.Regtest);
            var mainnetNetworkProvider = CreateNetworkProvider(ChainName.Mainnet);
            var testnetParser = new DerivationSchemeParser(testnetNetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));
            var mainnetParser = new DerivationSchemeParser(mainnetNetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));
            NBXplorer.DerivationStrategy.DerivationStrategyBase result;
            //  Passing electrum stuff
            // Passing a native segwit from mainnet to a testnet parser, means the testnet parser will try to convert it into segwit
            result = testnetParser.Parse(
                "zpub6nL6PUGurpU3DfPDSZaRS6WshpbNc9ctCFFzrCn54cssnheM31SZJZUcFHKtjJJNhAueMbh6ptFMfy1aeiMQJr3RJ4DDt1hAPx7sMTKV48t");
            Assert.Equal(
                "tpubD93CJNkmGjLXnsBqE2zGDqfEh1Q8iJ8wueordy3SeWt1RngbbuxXCsqASuVWFywmfoCwUE1rSfNJbaH4cBNcbp8WcyZgPiiRSTazLGL8U9w",
                result.ToString());
            result = mainnetParser.Parse(
                "zpub6nL6PUGurpU3DfPDSZaRS6WshpbNc9ctCFFzrCn54cssnheM31SZJZUcFHKtjJJNhAueMbh6ptFMfy1aeiMQJr3RJ4DDt1hAPx7sMTKV48t");
            Assert.Equal(
                "xpub68fZn8w5ZTP5X4zymr1B1vKsMtJUiudtN2DZHQzJJc87gW1tXh7S4SALCsQijUzXstg2reVyuZYFuPnTDKXNiNgDZNpNiC4BrVzaaGEaRHj",
                result.ToString());
            // P2SH
            result = testnetParser.Parse(
                "upub57Wa4MvRPNyAipy1MCpERxcFpHR2ZatyikppkyeWkoRL6QJvLVMo39jYdcaJVxyvBURyRVmErBEA5oGicKBgk1j72GAXSPFH5tUDoGZ8nEu");
            Assert.Equal(
                "tpubD6NzVbkrYhZ4YWjDJUACG9E8fJx2NqNY1iynTiPKEjJrzzRKAgha3nNnwGXr2BtvCJKJHW4nmG7rRqc2AGGy2AECgt16seMyV2FZivUmaJg-[p2sh]",
                result.ToString());

            result = mainnetParser.Parse(
                "ypub6QqdH2c5z79681jUgdxjGJzGW9zpL4ryPCuhtZE4GpvrJoZqM823XQN6iSQeVbbbp2uCRQ9UgpeMcwiyV6qjvxTWVcxDn2XEAnioMUwsrQ5");
            Assert.Equal(
                "xpub661MyMwAqRbcGiYMrHB74DtmLBrNPSsUU6PV7ALAtpYyFhkc6TrUuLhxhET4VgwgQPnPfvYvEAHojf7QmQRj8imudHFoC7hju4f9xxri8wR-[p2sh]",
                result.ToString());

            // if prefix not recognize, assume it is segwit
            result = testnetParser.Parse(
                "xpub661MyMwAqRbcGeVGU5e5KBcau1HHEUGf9Wr7k4FyLa8yRPNQrrVa7Ndrgg8Afbe2UYXMSL6tJBFd2JewwWASsePPLjkcJFL1tTVEs3UQ23X");
            Assert.Equal(
                "tpubD6NzVbkrYhZ4YSg7vGdAX6wxE8NwDrmih9SR6cK7gUtsAg37w5LfFpJgviCxC6bGGT4G3uckqH5fiV9ZLN1gm5qgQLVuymzFUR5ed7U7ksu",
                result.ToString());
            ////////////////

            var tpub =
                "tpubD6NzVbkrYhZ4Wc65tjhmcKdWFauAo7bGLRTxvggygkNyp6SMGutJp7iociwsinU33jyNBp1J9j2hJH5yQsayfiS3LEU2ZqXodAcnaygra8o";

            result = testnetParser.Parse(tpub);
            Assert.Equal(tpub, result.ToString());

            var regtestParser = new DerivationSchemeParser(regtestNetworkProvider.GetNetwork<BTCPayNetwork>("BTC"));
            var parsed =
                regtestParser.Parse(
                    "xpub6DG1rMYXiQtCc6CfdLFD9CtxqhzzRh7j6Sq6EdE9abgYy3cfDRrniLLv2AdwqHL1exiLnnKR5XXcaoiiexf3Y9R6J6rxkJtqJHzNzMW9QMZ-[p2sh]");
            Assert.Equal(
                "tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[p2sh]",
                parsed.ToString());

            // Let's make sure we can't generate segwit with dogecoin
            regtestParser = new DerivationSchemeParser(regtestNetworkProvider.GetNetwork<BTCPayNetwork>("DOGE"));
            parsed = regtestParser.Parse(
                "xpub6DG1rMYXiQtCc6CfdLFD9CtxqhzzRh7j6Sq6EdE9abgYy3cfDRrniLLv2AdwqHL1exiLnnKR5XXcaoiiexf3Y9R6J6rxkJtqJHzNzMW9QMZ-[p2sh]");
            Assert.Equal(
                "tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[legacy]",
                parsed.ToString());

            regtestParser = new DerivationSchemeParser(regtestNetworkProvider.GetNetwork<BTCPayNetwork>("DOGE"));
            parsed = regtestParser.Parse(
                "tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[p2sh]");
            Assert.Equal(
                "tpubDDdeNbNDRgqestPX5XEJM8ELAq6eR5cne5RPbBHHvWSSiLHNHehsrn1kGCijMnHFSsFFQMqHcdMfGzDL3pWHRasPMhcGRqZ4tFankQ3i4ok-[legacy]",
                parsed.ToString());

            //let's test output descriptor parsing support


            //we don't support every descriptor, only the ones which represent an HD wallet with stndard derivation paths
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("pk(0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798)"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("pkh(02c6047f9441ed7d6d3045406e95c07cd85c778e4b8cef3ca7abac09b95c709ee5)"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("wpkh(02f9308a019258c31049344f85f89d5229b531c845836f99b08601f113bce036f9)"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("sh(wpkh(03fff97bd5755eeea420453a14355235d382f6472f8568a18b2f057a1460297556))"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("combo(0279be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798)"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("sh(wsh(pkh(02e493dbf1c10d80f3581e4904930b1404cc6c13900ee0758474fa94abe8c4cd13)))"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("multi(1,022f8bde4d1a07209355b4a7250a5c5128e88b84bddc619ab7cba8d569b240efe4,025cbdf0646e5db4eaa398f365f2ea7a0e3d419b7e0330e39ce92bddedcac4f9bc)"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("sh(multi(2,022f01e5e15cca351daff3843fb70f3c2f0a1bdd05e5af888a67784ef3e10a2a01,03acd484e2f0c7f65309ad178a9f559abde09796974c57e714c35f110dfc27ccbe))"));
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("sh(sortedmulti(2,03acd484e2f0c7f65309ad178a9f559abde09796974c57e714c35f110dfc27ccbe,022f01e5e15cca351daff3843fb70f3c2f0a1bdd05e5af888a67784ef3e10a2a01))"));

            //let's see what we actually support now

            //standard legacy hd wallet
            var parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "pkh([d34db33f/44'/0'/0']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*)");
            Assert.Equal(KeyPath.Parse("44'/0'/0'"), Assert.Single(parsedDescriptor.Item2).KeyPath);
            Assert.Equal(HDFingerprint.Parse("d34db33f"), Assert.Single(parsedDescriptor.Item2).MasterFingerprint);
            Assert.Equal("xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[legacy]", parsedDescriptor.Item1.ToString());

            //masterfingerprint and key path are optional
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "pkh([d34db33f]xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*)");
            Assert.Equal("xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[legacy]", parsedDescriptor.Item1.ToString());
            //a master fingerprint must always be present if youre providing rooted path
            Assert.Throws<ParsingException>(() => mainnetParser.ParseOutputDescriptor("pkh([44'/0'/0']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/1/*)"));


            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "pkh(xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*)");
            Assert.Equal("xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[legacy]", parsedDescriptor.Item1.ToString());

            //but a different deriv path from standard (0/*) is not supported
            Assert.Throws<FormatException>(() => mainnetParser.ParseOutputDescriptor("pkh(xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/1/*)"));

            //p2sh-segwit hd wallet
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
               "sh(wpkh([d34db33f/49'/0'/0']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*))");
            Assert.Equal(KeyPath.Parse("49'/0'/0'"), Assert.Single(parsedDescriptor.Item2).KeyPath);
            Assert.Equal(HDFingerprint.Parse("d34db33f"), Assert.Single(parsedDescriptor.Item2).MasterFingerprint);
            Assert.Equal("xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[p2sh]", parsedDescriptor.Item1.ToString());

            //segwit hd wallet
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "wpkh([d34db33f/84'/0'/0']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*)");
            Assert.Equal(KeyPath.Parse("84'/0'/0'"), Assert.Single(parsedDescriptor.Item2).KeyPath);
            Assert.Equal(HDFingerprint.Parse("d34db33f"), Assert.Single(parsedDescriptor.Item2).MasterFingerprint);
            Assert.Equal("xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL", parsedDescriptor.Item1.ToString());

            //multisig tests

            //legacy
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "sh(multi(1,[d34db33f/45'/0]xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*,[d34db33f/45'/0]xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*))");
            Assert.Equal(2, parsedDescriptor.Item2.Length);
            var strat = Assert.IsType<MultisigDerivationStrategy>(Assert.IsType<P2SHDerivationStrategy>(parsedDescriptor.Item1).Inner);
            Assert.True(strat.IsLegacy);
            Assert.Equal(1, strat.RequiredSignatures);
            Assert.Equal(2, strat.Keys.Count());
            Assert.False(strat.LexicographicOrder);
            Assert.Equal("1-of-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[legacy]-[keeporder]", parsedDescriptor.Item1.ToString());

            //segwit
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "wsh(multi(1,[d34db33f/48'/0'/0'/2']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*,[d34db33f/48'/0'/0'/2']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*))");
            Assert.Equal(2, parsedDescriptor.Item2.Length);
            strat = Assert.IsType<MultisigDerivationStrategy>(Assert.IsType<P2WSHDerivationStrategy>(parsedDescriptor.Item1).Inner);
            Assert.False(strat.IsLegacy);
            Assert.Equal(1, strat.RequiredSignatures);
            Assert.Equal(2, strat.Keys.Count());
            Assert.False(strat.LexicographicOrder);
            Assert.Equal("1-of-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[keeporder]", parsedDescriptor.Item1.ToString());


            //segwit-p2sh
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "sh(wsh(multi(1,[d34db33f/48'/0'/0'/2']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*,[d34db33f/48'/0'/0'/2']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*)))");
            Assert.Equal(2, parsedDescriptor.Item2.Length);
            strat = Assert.IsType<MultisigDerivationStrategy>(Assert.IsType<P2WSHDerivationStrategy>(Assert.IsType<P2SHDerivationStrategy>(parsedDescriptor.Item1).Inner).Inner);
            Assert.False(strat.IsLegacy);
            Assert.Equal(1, strat.RequiredSignatures);
            Assert.Equal(2, strat.Keys.Count());
            Assert.False(strat.LexicographicOrder);
            Assert.Equal("1-of-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[keeporder]-[p2sh]", parsedDescriptor.Item1.ToString());

            //sorted
            parsedDescriptor = mainnetParser.ParseOutputDescriptor(
                "sh(sortedmulti(1,[d34db33f/48'/0'/0'/1']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*,[d34db33f/48'/0'/0'/1']xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL/0/*))");
            Assert.Equal("1-of-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-xpub6ERApfZwUNrhLCkDtcHTcxd75RbzS1ed54G1LkBUHQVHQKqhMkhgbmJbZRkrgZw4koxb5JaHWkY4ALHY2grBGRjaDMzQLcgJvLJuZZvRcEL-[legacy]", parsedDescriptor.Item1.ToString());
        }

        [Fact]
        [Trait("Altcoins", "Altcoins")]
        public void CanCalculateCryptoDue2()
        {
#pragma warning disable CS0618
            var dummy = new Key().PubKey.GetAddress(ScriptPubKeyType.Legacy, Network.RegTest).ToString();
            var networkProvider = CreateNetworkProvider(ChainName.Regtest);
            var networkBTC = networkProvider.GetNetwork("BTC");
            var networkLTC = networkProvider.GetNetwork("LTC");
            InvoiceEntity invoiceEntity = new InvoiceEntity();
            invoiceEntity.Networks = networkProvider;
            invoiceEntity.Payments = new System.Collections.Generic.List<PaymentEntity>();
            invoiceEntity.Price = 100;
            PaymentMethodDictionary paymentMethods = new PaymentMethodDictionary();
            paymentMethods.Add(new PaymentMethod() { Network = networkBTC, Currency = "BTC", Rate = 10513.44m, }
                .SetPaymentMethodDetails(
                    new BTCPayServer.Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod()
                    {
                        NextNetworkFee = Money.Coins(0.00000100m),
                        DepositAddress = dummy
                    }));
            paymentMethods.Add(new PaymentMethod() { Network = networkLTC, Currency = "LTC", Rate = 216.79m }
                .SetPaymentMethodDetails(
                    new BTCPayServer.Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod()
                    {
                        NextNetworkFee = Money.Coins(0.00010000m),
                        DepositAddress = dummy
                    }));
            invoiceEntity.SetPaymentMethods(paymentMethods);

            var btc = invoiceEntity.GetPaymentMethod(new PaymentMethodId("BTC", PaymentTypes.BTCLike));
            var accounting = btc.Calculate();

            invoiceEntity.Payments.Add(
                new PaymentEntity()
                {
                    Accounted = true,
                    Currency = "BTC",
                    NetworkFee = 0.00000100m,
                    Network = networkProvider.GetNetwork("BTC"),
                }
                    .SetCryptoPaymentData(new BitcoinLikePaymentData()
                    {
                        Network = networkProvider.GetNetwork("BTC"),
                        Output = new TxOut() { Value = Money.Coins(0.00151263m) }
                    }));
            invoiceEntity.UpdateTotals();
            accounting = btc.Calculate();
            invoiceEntity.Payments.Add(
                new PaymentEntity()
                {
                    Accounted = true,
                    Currency = "BTC",
                    NetworkFee = 0.00000100m,
                    Network = networkProvider.GetNetwork("BTC")
                }
                    .SetCryptoPaymentData(new BitcoinLikePaymentData()
                    {
                        Network = networkProvider.GetNetwork("BTC"),
                        Output = new TxOut() { Value = Money.Coins(accounting.Due) }
                    }));
            invoiceEntity.UpdateTotals();
            accounting = btc.Calculate();
            Assert.Equal(0.0m, accounting.Due);
            Assert.Equal(0.0m, accounting.DueUncapped);

            var ltc = invoiceEntity.GetPaymentMethod(new PaymentMethodId("LTC", PaymentTypes.BTCLike));
            accounting = ltc.Calculate();

            Assert.Equal(0.0m, accounting.Due);
            // LTC might should be over paid due to BTC paying above what it should (round 1 satoshi up), but we handle this case
            // and set DueUncapped to zero.
            Assert.Equal(0.0m, accounting.DueUncapped);
        }

        [Fact]
        public void AllPoliciesShowInUI()
        {
            new BitpayRateProvider(new System.Net.Http.HttpClient()).GetRatesAsync(default).GetAwaiter().GetResult();
            foreach (var policy in Policies.AllPolicies)
            {
                Assert.True(UIManageController.AddApiKeyViewModel.PermissionValueItem.PermissionDescriptions.ContainsKey(policy));
                if (Policies.IsStorePolicy(policy))
                {
                    Assert.True(UIManageController.AddApiKeyViewModel.PermissionValueItem.PermissionDescriptions.ContainsKey($"{policy}:"));
                }
            }
        }

        [Fact]
        public void CanParseMetadata()
        {
            var metadata = InvoiceMetadata.FromJObject(JObject.Parse("{\"posData\": {\"test\":\"a\"}}"));
            Assert.Equal(JObject.Parse("{\"test\":\"a\"}").ToString(), metadata.PosDataLegacy);
            Assert.Equal(JObject.Parse("{\"test\":\"a\"}").ToString(), metadata.PosData.ToString());

            // Legacy, as string
            metadata = InvoiceMetadata.FromJObject(JObject.Parse("{\"posData\": \"{\\\"test\\\":\\\"a\\\"}\"}"));
            Assert.Equal("{\"test\":\"a\"}", metadata.PosDataLegacy);
            Assert.Equal(JObject.Parse("{\"test\":\"a\"}").ToString(), metadata.PosData.ToString());

            metadata = InvoiceMetadata.FromJObject(JObject.Parse("{\"posData\": \"nobject\"}"));
            Assert.Equal("nobject", metadata.PosDataLegacy);
            Assert.Null(metadata.PosData);

            metadata = InvoiceMetadata.FromJObject(JObject.Parse("{\"posData\": null}"));
            Assert.Null(metadata.PosDataLegacy);
            Assert.Null(metadata.PosData);

            metadata = InvoiceMetadata.FromJObject(JObject.Parse("{}"));
            Assert.Null(metadata.PosDataLegacy);
            Assert.Null(metadata.PosData);
        }

        [Fact]
        public void CanParseInvoiceEntityDerivationStrategies()
        {
            // We have 3 ways of serializing the derivation strategies:
            // through "derivationStrategy", through "derivationStrategies" as a string, through "derivationStrategies" as JObject
            // Let's check that InvoiceEntity is similar in all cases.
            var legacy = new JObject()
            {
                ["derivationStrategy"] = "tpubDDLQZ1WMdy5YJAJWmRNoTJ3uQkavEPXCXnmD4eAuo9BKbzFUBbJmVHys5M3ku4Qw1C165wGpVWH55gZpHjdsCyntwNzhmCAzGejSL6rzbyf"
            };
            var scheme = DerivationSchemeSettings.Parse("tpubDDLQZ1WMdy5YJAJWmRNoTJ3uQkavEPXCXnmD4eAuo9BKbzFUBbJmVHys5M3ku4Qw1C165wGpVWH55gZpHjdsCyntwNzhmCAzGejSL6rzbyf", CreateNetworkProvider(ChainName.Regtest).BTC);
            Assert.True(scheme.AccountDerivation is DirectDerivationStrategy { Segwit: true });
            scheme.Source = "ManualDerivationScheme";
            scheme.AccountOriginal = "tpubDDLQZ1WMdy5YJAJWmRNoTJ3uQkavEPXCXnmD4eAuo9BKbzFUBbJmVHys5M3ku4Qw1C165wGpVWH55gZpHjdsCyntwNzhmCAzGejSL6rzbyf";
            var legacy2 = new JObject()
            {
                ["derivationStrategies"] = scheme.ToJson()
            };

            var newformat = new JObject()
            {
                ["derivationStrategies"] = JObject.Parse(scheme.ToJson())
            };

            //new BTCPayNetworkProvider(ChainName.Regtest)
#pragma warning disable CS0618 // Type or member is obsolete
            var formats = new[] { legacy, legacy2, newformat }
            .Select(o =>
            {
                var entity = JsonConvert.DeserializeObject<InvoiceEntity>(o.ToString());
                entity.Networks = CreateNetworkProvider(ChainName.Regtest);
                return entity.DerivationStrategies.ToString();
            })
            .ToHashSet();
#pragma warning restore CS0618 // Type or member is obsolete
            Assert.Single(formats);
        }

        [Fact]
        public void PaymentMethodIdConverterIsGraceful()
        {
            var pmi = "\"BTC_hasjdfhasjkfjlajn\"";
            JsonTextReader reader = new(new StringReader(pmi));
            reader.Read();
            Assert.Null(new PaymentMethodIdJsonConverter().ReadJson(reader, typeof(PaymentMethodId), null,
                JsonSerializer.CreateDefault()));
        }

        [Fact]
        public void CanBeBracefulAfterObsoleteShitcoin()
        {
            var blob = new StoreBlob();
            blob.PaymentMethodCriteria = new List<PaymentMethodCriteria>()
            {
                new()
                {
                    Above = true,
                    Value = new CurrencyValue() {Currency = "BTC", Value = 0.1m},
                    PaymentMethod = new PaymentMethodId("BTC", PaymentTypes.BTCLike)
                }
            };
            var newBlob = new Serializer(null).ToString(blob).Replace("paymentMethod\":\"BTC\"", "paymentMethod\":\"ETH_ZYC\"");
            Assert.Empty(StoreDataExtensions.GetStoreBlob(new StoreData() { StoreBlob = newBlob }).PaymentMethodCriteria);
        }
    }
}
