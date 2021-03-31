using System;
using System.Collections.Generic;
using System.Globalization;
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
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Hosting;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payments.PayJoin.Sender;
using BTCPayServer.Rating;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using BTCPayServer.Tests.Logging;
using BTCPayServer.U2F.Models;
using BTCPayServer.Validation;
using ExchangeSharp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using NBitpayClient;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using RatesViewModel = BTCPayServer.Models.StoreViewModels.RatesViewModel;

namespace BTCPayServer.Tests
{
    public class UnitTest1
    {
        public const int TestTimeout = 60_000;

        public UnitTest1(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
            Logs.LogProvider = new XUnitLogProvider(helper);
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
                checkLinks.Add(CheckLinks(regex, httpClient, file));
            }

            await Task.WhenAll(checkLinks);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CheckSwaggerIsConformToSchema()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();

                var sresp = Assert
                    .IsType<JsonResult>(await tester.PayTester.GetController<HomeController>(acc.UserId, acc.StoreId)
                        .Swagger()).Value.ToJson();

                JObject swagger = JObject.Parse(sresp);
                using HttpClient client = new HttpClient();
                var resp = await client.GetAsync(
                    "https://raw.githubusercontent.com/OAI/OpenAPI-Specification/master/schemas/v3.0/schema.json");
                var schema = JSchema.Parse(await resp.Content.ReadAsStringAsync());
                IList<ValidationError> errors;
                bool valid = swagger.IsValid(schema, out errors);
                //the schema is not fully compliant to the spec. We ARE allowed to have multiple security schemas. 
                var matchedError = errors.Where(error =>
                    error.Path == "components.securitySchemes.Basic" && error.ErrorType == ErrorType.OneOf).ToList();
                foreach (ValidationError validationError in matchedError)
                {
                    errors.Remove(validationError);
                }
                valid = !errors.Any();

                Assert.Empty(errors);
                Assert.True(valid);
            }
        }
        
        [Fact]
        [Trait("Integration", "Integration")]
        public async Task EnsureSwaggerPermissionsDocumented()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();

                var description =
                    "BTCPay Server supports authenticating and authorizing users through an API Key that is generated by them. Send the API Key as a header value to Authorization with the format: `token {token}`. For a smoother experience, you can generate a url that redirects users to an API key creation screen.\n\n The following permissions are available to the context of the user creating the API Key:\n\n#OTHERPERMISSIONS#\n\nThe following permissions are available if the user is an administrator:\n\n#SERVERPERMISSIONS#\n\nThe following permissions applies to all stores of the user, you can limit to a specific store with the following format: `btcpay.store.cancreateinvoice:6HSHAEU4iYWtjxtyRs9KyPjM9GAQp8kw2T9VWbGG1FnZ`:\n\n#STOREPERMISSIONS#\n\nNote that API Keys only limits permission of a user and can never expand it. If an API Key has the permission `btcpay.server.canmodifyserversettings` but that the user account creating this API Key is not administrator, the API Key will not be able to modify the server settings.\n";
                
                var storePolicies =
                    ManageController.AddApiKeyViewModel.PermissionValueItem.PermissionDescriptions.Where(pair =>
                        Policies.IsStorePolicy(pair.Key) && !pair.Key.EndsWith(":", StringComparison.InvariantCulture));
                var serverPolicies =
                    ManageController.AddApiKeyViewModel.PermissionValueItem.PermissionDescriptions.Where(pair =>
                        Policies.IsServerPolicy(pair.Key));
                var otherPolicies =
                    ManageController.AddApiKeyViewModel.PermissionValueItem.PermissionDescriptions.Where(pair =>
                        !Policies.IsStorePolicy(pair.Key) && !Policies.IsServerPolicy(pair.Key));

                description = description.Replace("#OTHERPERMISSIONS#",
                        string.Join("\n", otherPolicies.Select(pair => $"* `{pair.Key}`: {pair.Value.Title}")))
                    .Replace("#SERVERPERMISSIONS#",
                        string.Join("\n", serverPolicies.Select(pair => $"* `{pair.Key}`: {pair.Value.Title}")))
                    .Replace("#STOREPERMISSIONS#",
                        string.Join("\n", storePolicies.Select(pair => $"* `{pair.Key}`: {pair.Value.Title}")));
                Logs.Tester.LogInformation(description);
                                
                var sresp = Assert
                    .IsType<JsonResult>(await tester.PayTester.GetController<HomeController>(acc.UserId, acc.StoreId)
                        .Swagger()).Value.ToJson();

                JObject json = JObject.Parse(sresp);

                Assert.Equal(description, json["components"]["securitySchemes"]["API Key"]["description"].Value<string>());
            }
        }

        private static async Task CheckLinks(Regex regex, HttpClient httpClient, string file)
        {
            List<Task> checkLinks = new List<Task>();
            var text = await File.ReadAllTextAsync(file);

            var urlBlacklist = new string[]
            {
                "https://www.btse.com", // not allowing to be hit from circleci
                "https://www.bitpay.com" // not allowing to be hit from circleci
            };

            foreach (var match in regex.Matches(text).OfType<Match>())
            {
                var url = match.Groups[1].Value;
                if (urlBlacklist.Any(a => a.StartsWith(url.ToLowerInvariant())))
                    continue;

                checkLinks.Add(AssertLinkNotDead(httpClient, url, file));
            }

            await Task.WhenAll(checkLinks);
        }

        private static async Task AssertLinkNotDead(HttpClient httpClient, string url, string file)
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
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                if (uri.Fragment.Length != 0)
                {
                    var fragment = uri.Fragment.Substring(1);
                    var contents = await response.Content.ReadAsStringAsync();
                    Assert.Matches($"id=\"{fragment}\"", contents);
                }

                Logs.Tester.LogInformation($"OK: {url} ({file})");
            }
            catch (Exception ex) when (ex is MatchesException)
            {
                var details = ex.Message;
                Logs.Tester.LogInformation($"FAILED: {url} ({file}) – anchor not found: {uri.Fragment}");

                throw;
            }
            catch (Exception ex)
            {
                var details = ex is EqualException ? (ex as EqualException).Actual : ex.Message;
                Logs.Tester.LogInformation($"FAILED: {url} ({file}) {details}");

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
        [Trait("Integration", "Integration")]
        public async Task CanUseTestWebsiteUI()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var response = await tester.PayTester.HttpClient.GetAsync("");
                Assert.True(response.IsSuccessStatusCode);
            }
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
        [Trait("Integration", "Integration")]
        public async Task CanAcceptInvoiceWithTolerance2()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                // Set tolerance to 50%
                var stores = user.GetController<StoresController>();
                var vm = Assert.IsType<StoreViewModel>(Assert.IsType<ViewResult>(stores.UpdateStore()).Model);
                Assert.Equal(0.0, vm.PaymentTolerance);
                vm.PaymentTolerance = 50.0;
                Assert.IsType<RedirectToActionResult>(stores.UpdateStore(vm).Result);

                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Buyer = new Buyer() { email = "test@fwf.com" },
                        Price = 5000.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                // Pays 75%
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, tester.ExplorerNode.Network);
                tester.ExplorerNode.SendToAddress(invoiceAddress,
                    Money.Satoshis(invoice.BtcDue.Satoshi * 0.75m));

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                });
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanThrowBitpay404Error()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Buyer = new Buyer() { email = "test@fwf.com" },
                        Price = 5000.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                try
                {
                    var throwsBitpay404Error = user.BitPay.GetInvoice(invoice.Id + "123");
                }
                catch (BitPayException ex)
                {
                    Assert.Equal("Object not found", ex.Errors.First());
                }
                var req = new HttpRequestMessage(HttpMethod.Get, "/invoices/Cy9jfK82eeEED1T3qhwF3Y");
                req.Headers.TryAddWithoutValidation("Authorization", "Basic dGVzdA==");
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                var result = await tester.PayTester.HttpClient.SendAsync(req);
                Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
                Assert.Equal(0, result.Content.Headers.ContentLength.Value);
            }
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
                (1000.0001m, "₹ 1,000.00 (INR)", "INR")
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
                new BTCPayServerOptions() { TorrcFile = TestUtils.GetTestDataFullPath("Tor/torrc") });
            await tor.Refresh();

            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.BTCPayServer));
            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.P2P));
            Assert.Single(tor.Services.Where(t => t.ServiceType == TorServiceType.RPC));
            Assert.True(tor.Services.Where(t => t.ServiceType == TorServiceType.Other).Count() > 1);
        }


        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task EnsureNewLightningInvoiceOnPartialPayment()
        {
            using var tester = ServerTester.Create();
            tester.ActivateLightning();
            await tester.StartAsync();
            await tester.EnsureChannelsSetup();
            var user = tester.NewAccount();
            await user.GrantAccessAsync(true);
            await user.RegisterDerivationSchemeAsync("BTC");
            await user.RegisterLightningNodeAsync("BTC", LightningConnectionType.CLightning);
            user.SetNetworkFeeMode(NetworkFeeMode.Never);
            await user.ModifyStoreAsync(model => model.SpeedPolicy = SpeedPolicy.HighSpeed);
            var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(0.0001m, "BTC"));
            await tester.WaitForEvent<InvoiceNewPaymentDetailsEvent>(async () =>
            {
                await tester.ExplorerNode.SendToAddressAsync(
                    BitcoinAddress.Create(invoice.BitcoinAddress, Network.RegTest), Money.Coins(0.00005m));
            }, e => e.InvoiceId == invoice.Id && e.PaymentMethodId.PaymentType == LightningPaymentType.Instance );
            await tester.ExplorerNode.GenerateAsync(1);
            await Task.Delay(100); // wait a bit for payment to process before fetching new invoice
            var newInvoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
            var newBolt11 = newInvoice.CryptoInfo.First(o => o.PaymentUrls.BOLT11 != null).PaymentUrls.BOLT11;
            var oldBolt11 = invoice.CryptoInfo.First(o => o.PaymentUrls.BOLT11 != null).PaymentUrls.BOLT11;
            Assert.NotEqual(newBolt11, oldBolt11);
            Assert.Equal(newInvoice.BtcDue.GetValue(), BOLT11PaymentRequest.Parse(newBolt11, Network.RegTest).MinimumAmount.ToDecimal(LightMoneyUnit.BTC));

            Logs.Tester.LogInformation($"Paying invoice {newInvoice.Id} remaining due amount {newInvoice.BtcDue.GetValue()} via lightning");
            var evt = await tester.WaitForEvent<InvoiceDataChangedEvent>(async () =>
            {
                await tester.SendLightningPaymentAsync(newInvoice);
            }, evt => evt.InvoiceId == invoice.Id);

            var fetchedInvoice = await tester.PayTester.InvoiceRepository.GetInvoice(evt.InvoiceId);
            Assert.Contains(fetchedInvoice.Status, new[] { InvoiceStatusLegacy.Complete, InvoiceStatusLegacy.Confirmed });
            Assert.Equal(InvoiceExceptionStatus.None, fetchedInvoice.ExceptionStatus);

            Logs.Tester.LogInformation($"Paying invoice {invoice.Id} original full amount bolt11 invoice ");
            evt = await tester.WaitForEvent<InvoiceDataChangedEvent>(async () =>
            {
                await tester.SendLightningPaymentAsync(invoice);
            }, evt => evt.InvoiceId == invoice.Id);
            Assert.Equal(evt.InvoiceId, invoice.Id);
            fetchedInvoice = await tester.PayTester.InvoiceRepository.GetInvoice(evt.InvoiceId);
            Assert.Equal(3, fetchedInvoice.Payments.Count);
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSetLightningServer()
        {
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLightning();
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess(true);
                var storeController = user.GetController<StoresController>();
                Assert.IsType<ViewResult>(storeController.UpdateStore());
                Assert.IsType<ViewResult>(storeController.AddLightningNode(user.StoreId, "BTC"));

                var testResult = storeController.AddLightningNode(user.StoreId, new LightningNodeViewModel()
                {
                    ConnectionString = $"type=charge;server={tester.MerchantCharge.Client.Uri.AbsoluteUri};allowinsecure=true",
                    SkipPortTest = true // We can't test this as the IP can't be resolved by the test host :(
                }, "test", "BTC").GetAwaiter().GetResult();
                Assert.False(storeController.TempData.ContainsKey(WellKnownTempData.ErrorMessage));
                storeController.TempData.Clear();
                Assert.True(storeController.ModelState.IsValid);

                Assert.IsType<RedirectToActionResult>(storeController.AddLightningNode(user.StoreId,
                    new LightningNodeViewModel()
                    {
                        ConnectionString = $"type=charge;server={tester.MerchantCharge.Client.Uri.AbsoluteUri};allowinsecure=true"
                    }, "save", "BTC").GetAwaiter().GetResult());

                // Make sure old connection string format does not work
                Assert.IsType<ViewResult>(storeController.AddLightningNode(user.StoreId,
                    new LightningNodeViewModel() { ConnectionString = tester.MerchantCharge.Client.Uri.AbsoluteUri },
                    "save", "BTC").GetAwaiter().GetResult());

                var storeVm =
                    Assert.IsType<Models.StoreViewModels.StoreViewModel>(Assert
                        .IsType<ViewResult>(storeController.UpdateStore()).Model);
                Assert.Single(storeVm.LightningNodes.Where(l => !string.IsNullOrEmpty(l.Address)));
            }
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSendLightningPaymentCLightning()
        {
            await ProcessLightningPayment(LightningConnectionType.CLightning);
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSendLightningPaymentCharge()
        {
            await ProcessLightningPayment(LightningConnectionType.Charge);
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSendLightningPaymentLnd()
        {
            await ProcessLightningPayment(LightningConnectionType.LndREST);
        }

        async Task ProcessLightningPayment(LightningConnectionType type)
        {
            // For easier debugging and testing
            // LightningLikePaymentHandler.LIGHTNING_TIMEOUT = int.MaxValue;

            using (var tester = ServerTester.Create())
            {
                tester.ActivateLightning();
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess(true);
                user.RegisterLightningNode("BTC", type);
                user.RegisterDerivationScheme("BTC");

                await CanSendLightningPaymentCore(tester, user);

                await Task.WhenAll(Enumerable.Range(0, 5)
                    .Select(_ => CanSendLightningPaymentCore(tester, user))
                    .ToArray());
            }
        }
        async Task CanSendLightningPaymentCore(ServerTester tester, TestAccount user)
        {
            var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice()
            {
                Price = 0.01m,
                Currency = "USD",
                PosData = "posData",
                OrderId = "orderId",
                ItemDesc = "Some description"
            });
            await Task.Delay(TimeSpan.FromMilliseconds(1000)); // Give time to listen the new invoices
            Logs.Tester.LogInformation($"Trying to send Lightning payment to {invoice.Id}");
            await tester.SendLightningPaymentAsync(invoice);
            Logs.Tester.LogInformation($"Lightning payment to {invoice.Id} is sent");
            await TestUtils.EventuallyAsync(async () =>
            {
                var localInvoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
                Assert.Equal("complete", localInvoice.Status);
                // C-Lightning may overpay for privacy
                Assert.Contains(localInvoice.ExceptionStatus.ToString(), new[] { "False", "paidOver" });
            });
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseServerInitiatedPairingCode()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.Register();
                acc.CreateStore();

                var controller = acc.GetController<StoresController>();
                var token = (RedirectToActionResult)await controller.CreateToken2(
                    new Models.StoreViewModels.CreateTokenViewModel()
                    {
                        Label = "bla",
                        PublicKey = null,
                        StoreId = acc.StoreId
                    });

                var pairingCode = (string)token.RouteValues["pairingCode"];

                acc.BitPay.AuthorizeClient(new PairingCode(pairingCode)).GetAwaiter().GetResult();
                Assert.True(acc.BitPay.TestAccess(Facade.Merchant));
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanSendIPN()
        {
            using (var callbackServer = new CustomServer())
            {
                using (var tester = ServerTester.Create())
                {
                    await tester.StartAsync();
                    var acc = tester.NewAccount();
                    acc.GrantAccess();
                    acc.RegisterDerivationScheme("BTC");
                    acc.ModifyStore(s => s.SpeedPolicy = SpeedPolicy.LowSpeed);
                    var invoice = acc.BitPay.CreateInvoice(new Invoice()
                    {
                        Price = 5.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        NotificationURL = callbackServer.GetUri().AbsoluteUri,
                        ItemDesc = "Some description",
                        FullNotifications = true,
                        ExtendedNotifications = true
                    });
                    BitcoinUrlBuilder url = new BitcoinUrlBuilder(invoice.PaymentUrls.BIP21,
                        tester.NetworkProvider.BTC.NBitcoinNetwork);
                    bool receivedPayment = false;
                    bool paid = false;
                    bool confirmed = false;
                    bool completed = false;
                    while (!completed || !confirmed)
                    {
                        var request = await callbackServer.GetNextRequest();
                        if (request.ContainsKey("event"))
                        {
                            var evtName = request["event"]["name"].Value<string>();
                            switch (evtName)
                            {
                                case InvoiceEvent.Created:
                                    tester.ExplorerNode.SendToAddress(url.Address, url.Amount);
                                    break;
                                case InvoiceEvent.ReceivedPayment:
                                    receivedPayment = true;
                                    break;
                                case InvoiceEvent.PaidInFull:
                                    Assert.True(receivedPayment);
                                    tester.ExplorerNode.Generate(6);
                                    paid = true;
                                    break;
                                case InvoiceEvent.Confirmed:
                                    Assert.True(paid);
                                    confirmed = true;
                                    break;
                                case InvoiceEvent.Completed:
                                    Assert.True(
                                        paid); //TODO: Fix, out of order event mean we can receive invoice_confirmed after invoice_complete
                                    completed = true;
                                    break;
                                default:
                                    Assert.False(true, $"{evtName} was not expected");
                                    break;
                            }
                        }
                    }
                    var invoice2 = acc.BitPay.GetInvoice(invoice.Id);
                    Assert.NotNull(invoice2);
                }
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CantPairTwiceWithSamePubkey()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.Register();
                acc.CreateStore();
                var store = acc.GetController<StoresController>();
                var pairingCode = acc.BitPay.RequestClientAuthorization("test", Facade.Merchant);
                Assert.IsType<RedirectToActionResult>(store.Pair(pairingCode.ToString(), acc.StoreId).GetAwaiter()
                    .GetResult());

                pairingCode = acc.BitPay.RequestClientAuthorization("test1", Facade.Merchant);
                acc.CreateStore();
                var store2 = acc.GetController<StoresController>();
                await store2.Pair(pairingCode.ToString(), store2.CurrentStore.Id);
                Assert.Contains(nameof(PairingResult.ReusedKey),
                    (string)store2.TempData[WellKnownTempData.ErrorMessage], StringComparison.CurrentCultureIgnoreCase);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
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


        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseTorClient()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var proxy = tester.PayTester.GetService<Socks5HttpProxyServer>();
                void AssertConnectionDropped()
                {
                    TestUtils.Eventually(() =>
                    {
                        Thread.MemoryBarrier();
                        Assert.Equal(0, proxy.ConnectionCount);
                    });
                }
                var httpFactory = tester.PayTester.GetService<IHttpClientFactory>();
                var client = httpFactory.CreateClient(PayjoinServerCommunicator.PayjoinOnionNamedClient);
                Assert.NotNull(client);
                var response = await client.GetAsync("https://check.torproject.org/");
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain("You are not using Tor.", result);
                Assert.Contains("Congratulations. This browser is configured to use Tor.", result);
                AssertConnectionDropped();
                response = await client.GetAsync("http://explorerzydxu5ecjrkwceayqybizmpjjznk5izmitf2modhcusuqlid.onion/");
                response.EnsureSuccessStatusCode();
                result = await response.Content.ReadAsStringAsync();
                Assert.Contains("Bitcoin", result);

                AssertConnectionDropped();
                response = await client.GetAsync("http://explorerzydxu5ecjrkwceayqybizmpjjznk5izmitf2modhcusuqlid.onion/");
                response.EnsureSuccessStatusCode();
                AssertConnectionDropped();
                client.Dispose();
                AssertConnectionDropped();
                client = httpFactory.CreateClient(PayjoinServerCommunicator.PayjoinOnionNamedClient);
                response = await client.GetAsync("http://explorerzydxu5ecjrkwceayqybizmpjjznk5izmitf2modhcusuqlid.onion/");
                response.EnsureSuccessStatusCode();
                AssertConnectionDropped();

                Logs.Tester.LogInformation("Querying an onin address which can't be found should send http 500");
                response = await client.GetAsync("http://dwoduwoi.onion/");
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                AssertConnectionDropped();

                Logs.Tester.LogInformation("Querying valid onion but unreachable should send error 502");
                response = await client.GetAsync("http://fastrcl5totos3vekjbqcmgpnias5qytxnaj7gpxtxhubdcnfrkapqad.onion/");
                Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
                AssertConnectionDropped();
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanRescanWallet()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.GrantAccess();
                acc.RegisterDerivationScheme("BTC", ScriptPubKeyType.Segwit);
                var btcDerivationScheme = acc.DerivationScheme;

                var walletController = acc.GetController<WalletsController>();

                var walletId = new WalletId(acc.StoreId, "BTC");
                acc.IsAdmin = true;
                walletController = acc.GetController<WalletsController>();

                var rescan =
                    Assert.IsType<RescanWalletModel>(Assert
                        .IsType<ViewResult>(walletController.WalletRescan(walletId).Result).Model);
                Assert.True(rescan.Ok);
                Assert.True(rescan.IsFullySync);
                Assert.True(rescan.IsSupportedByCurrency);
                Assert.True(rescan.IsServerAdmin);

                rescan.GapLimit = 100;

                // Sending a coin
                var txId = tester.ExplorerNode.SendToAddress(
                    btcDerivationScheme.GetDerivation(new KeyPath("0/90")).ScriptPubKey, Money.Coins(1.0m));
                tester.ExplorerNode.Generate(1);
                var transactions = Assert.IsType<ListTransactionsViewModel>(Assert
                    .IsType<ViewResult>(walletController.WalletTransactions(walletId).Result).Model);
                Assert.Empty(transactions.Transactions);

                Assert.IsType<RedirectToActionResult>(walletController.WalletRescan(walletId, rescan).Result);

                while (true)
                {
                    rescan = Assert.IsType<RescanWalletModel>(Assert
                        .IsType<ViewResult>(walletController.WalletRescan(walletId).Result).Model);
                    if (rescan.Progress == null && rescan.LastSuccess != null)
                    {
                        if (rescan.LastSuccess.Found == 0)
                            continue;
                        // Scan over
                        break;
                    }
                    else
                    {
                        Assert.Null(rescan.TimeOfScan);
                        Assert.NotNull(rescan.RemainingTime);
                        Assert.NotNull(rescan.Progress);
                        Thread.Sleep(100);
                    }
                }

                Assert.Null(rescan.PreviousError);
                Assert.NotNull(rescan.TimeOfScan);
                Assert.Equal(1, rescan.LastSuccess.Found);
                transactions = Assert.IsType<ListTransactionsViewModel>(Assert
                    .IsType<ViewResult>(walletController.WalletTransactions(walletId).Result).Model);
                var tx = Assert.Single(transactions.Transactions);
                Assert.Equal(tx.Id, txId.ToString());

                // Hijack the test to see if we can add label and comments
                Assert.IsType<RedirectToActionResult>(
                    await walletController.ModifyTransaction(walletId, tx.Id, addcomment: "hello-pouet"));
                Assert.IsType<RedirectToActionResult>(
                    await walletController.ModifyTransaction(walletId, tx.Id, addlabel: "test"));
                Assert.IsType<RedirectToActionResult>(
                    await walletController.ModifyTransaction(walletId, tx.Id, addlabelclick: "test2"));
                Assert.IsType<RedirectToActionResult>(
                    await walletController.ModifyTransaction(walletId, tx.Id, addcomment: "hello"));

                transactions = Assert.IsType<ListTransactionsViewModel>(Assert
                    .IsType<ViewResult>(walletController.WalletTransactions(walletId).Result).Model);
                tx = Assert.Single(transactions.Transactions);

                Assert.Equal("hello", tx.Comment);
                Assert.Contains("test", tx.Labels.Select(l => l.Text));
                Assert.Contains("test2", tx.Labels.Select(l => l.Text));
                Assert.Equal(2, tx.Labels.GroupBy(l => l.Color).Count());

                Assert.IsType<RedirectToActionResult>(
                    await walletController.ModifyTransaction(walletId, tx.Id, removelabel: "test2"));

                transactions = Assert.IsType<ListTransactionsViewModel>(Assert
                    .IsType<ViewResult>(walletController.WalletTransactions(walletId).Result).Model);
                tx = Assert.Single(transactions.Transactions);

                Assert.Equal("hello", tx.Comment);
                Assert.Contains("test", tx.Labels.Select(l => l.Text));
                Assert.DoesNotContain("test2", tx.Labels.Select(l => l.Text));
                Assert.Single(tx.Labels.GroupBy(l => l.Color));

                var walletInfo = await tester.PayTester.GetService<WalletRepository>().GetWalletInfo(walletId);
                Assert.Single(walletInfo.LabelColors); // the test2 color should have been removed
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanListInvoices()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.GrantAccess();
                acc.RegisterDerivationScheme("BTC");
                // First we try payment with a merchant having only BTC
                var invoice = acc.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 500,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                var cashCow = tester.ExplorerNode;
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, cashCow.Network);
                var firstPayment = invoice.CryptoInfo[0].TotalDue - Money.Satoshis(10);
                cashCow.SendToAddress(invoiceAddress, firstPayment);
                TestUtils.Eventually(() =>
                {
                    invoice = acc.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(firstPayment, invoice.CryptoInfo[0].Paid);
                });


                AssertSearchInvoice(acc, true, invoice.Id, $"storeid:{acc.StoreId}");
                AssertSearchInvoice(acc, false, invoice.Id, $"storeid:blah");
                AssertSearchInvoice(acc, true, invoice.Id, $"{invoice.Id}");
                AssertSearchInvoice(acc, true, invoice.Id, $"exceptionstatus:paidPartial");
                AssertSearchInvoice(acc, false, invoice.Id, $"exceptionstatus:paidOver");
                AssertSearchInvoice(acc, true, invoice.Id, $"unusual:true");
                AssertSearchInvoice(acc, false, invoice.Id, $"unusual:false");

                var time = invoice.InvoiceTime;
                AssertSearchInvoice(acc, true, invoice.Id, $"startdate:{time.ToString("yyyy-MM-dd HH:mm:ss")}");
                AssertSearchInvoice(acc, true, invoice.Id, $"enddate:{time.ToStringLowerInvariant()}");
                AssertSearchInvoice(acc, false, invoice.Id,
                    $"startdate:{time.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss")}");
                AssertSearchInvoice(acc, false, invoice.Id,
                    $"enddate:{time.AddSeconds(-1).ToString("yyyy-MM-dd HH:mm:ss")}");
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanListNotifications()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.GrantAccess(true);
                acc.RegisterDerivationScheme("BTC");

                const string newVersion = "1.0.4.4";
                var ctrl = acc.GetController<NotificationsController>();
                var resp = await ctrl.Generate(newVersion);

                var vm = Assert.IsType<Models.NotificationViewModels.IndexViewModel>(
                    Assert.IsType<ViewResult>(await ctrl.Index()).Model);

                Assert.True(vm.Skip == 0);
                Assert.True(vm.Count == 50);
                Assert.True(vm.Total == 1);
                Assert.True(vm.Items.Count == 1);

                var fn = vm.Items.First();
                var now = DateTimeOffset.UtcNow;
                Assert.True(fn.Created >= now.AddSeconds(-3));
                Assert.True(fn.Created <= now);
                Assert.Equal($"New version {newVersion} released!", fn.Body);
                Assert.Equal($"https://github.com/btcpayserver/btcpayserver/releases/tag/v{newVersion}", fn.ActionLink);
                Assert.False(fn.Seen);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanGetRates()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var acc = tester.NewAccount();
                acc.GrantAccess();
                acc.RegisterDerivationScheme("BTC");

                var rateController = acc.GetController<RateController>();
                var GetBaseCurrencyRatesResult = JObject.Parse(((JsonResult)rateController
                    .GetBaseCurrencyRates("BTC", default)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate[]>>();
                Assert.NotNull(GetBaseCurrencyRatesResult);
                Assert.NotNull(GetBaseCurrencyRatesResult.Data);
                var rate = Assert.Single(GetBaseCurrencyRatesResult.Data);
                Assert.Equal("BTC", rate.Code);

                var GetRatesResult = JObject.Parse(((JsonResult)rateController.GetRates(null, default)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate[]>>();
                // We don't have any default currencies, so this should be failing
                Assert.Null(GetRatesResult?.Data);

                var store = acc.GetController<StoresController>();
                var ratesVM = (RatesViewModel)(Assert.IsType<ViewResult>(store.Rates()).Model);
                ratesVM.DefaultCurrencyPairs = "BTC_USD,LTC_USD";
                await store.Rates(ratesVM);
                store = acc.GetController<StoresController>();
                rateController = acc.GetController<RateController>();
                GetRatesResult = JObject.Parse(((JsonResult)rateController.GetRates(null, default)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate[]>>();
                // Now we should have a result
                Assert.NotNull(GetRatesResult);
                Assert.NotNull(GetRatesResult.Data);
                Assert.Equal(2, GetRatesResult.Data.Length);

                var GetCurrencyPairRateResult = JObject.Parse(((JsonResult)rateController
                    .GetCurrencyPairRate("BTC", "LTC", default)
                    .GetAwaiter().GetResult()).Value.ToJson()).ToObject<DataWrapper<Rate>>();

                Assert.NotNull(GetCurrencyPairRateResult);
                Assert.NotNull(GetCurrencyPairRateResult.Data);
                Assert.Equal("LTC", GetCurrencyPairRateResult.Data.Code);

                // Should be OK because the request is signed, so we can know the store
                var rates = acc.BitPay.GetRates();
                HttpClient client = new HttpClient();
                // Unauthentified requests should also be ok
                var response =
                    await client.GetAsync($"http://127.0.0.1:{tester.PayTester.Port}/api/rates?storeId={acc.StoreId}");
                response.EnsureSuccessStatusCode();
                response = await client.GetAsync(
                    $"http://127.0.0.1:{tester.PayTester.Port}/rates?storeId={acc.StoreId}");
                response.EnsureSuccessStatusCode();
            }
        }

        private void AssertSearchInvoice(TestAccount acc, bool expected, string invoiceId, string filter)
        {
            var result =
                (Models.InvoicingModels.InvoicesModel)((ViewResult)acc.GetController<InvoiceController>()
                    .ListInvoices(new InvoicesModel { SearchTerm = filter }).Result).Model;
            Assert.Equal(expected, result.Invoices.Any(i => i.InvoiceId == invoiceId));
        }

        // [Fact(Timeout = TestTimeout)]
        [Fact()]
        [Trait("Integration", "Integration")]
        public async Task CanRBFPayment()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                user.SetNetworkFeeMode(NetworkFeeMode.Always);
                var invoice =
                    user.BitPay.CreateInvoice(new Invoice() { Price = 5000.0m, Currency = "USD" }, Facade.Merchant);
                var payment1 = invoice.BtcDue + Money.Coins(0.0001m);
                var payment2 = invoice.BtcDue;

                var tx1 = new uint256(tester.ExplorerNode.SendCommand("sendtoaddress", new object[]
                {
                    invoice.BitcoinAddress, payment1.ToString(), null, //comment
                    null, //comment_to
                    false, //subtractfeefromamount
                    true, //replaceable
                }).ResultString);
                Logs.Tester.LogInformation(
                    $"Let's send a first payment of {payment1} for the {invoice.BtcDue} invoice ({tx1})");
                var invoiceAddress =
                    BitcoinAddress.Create(invoice.BitcoinAddress, user.SupportedNetwork.NBitcoinNetwork);

                Logs.Tester.LogInformation($"The invoice should be paidOver");
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(payment1, invoice.BtcPaid);
                    Assert.Equal("paid", invoice.Status);
                    Assert.Equal("paidOver", invoice.ExceptionStatus.ToString());
                    invoiceAddress =
                        BitcoinAddress.Create(invoice.BitcoinAddress, user.SupportedNetwork.NBitcoinNetwork);
                });

                var tx = tester.ExplorerNode.GetRawTransaction(new uint256(tx1));
                foreach (var input in tx.Inputs)
                {
                    input.ScriptSig = Script.Empty; //Strip signatures
                }

                var output = tx.Outputs.First(o => o.Value == payment1);
                output.Value = payment2;
                output.ScriptPubKey = invoiceAddress.ScriptPubKey;

                using (var cts = new CancellationTokenSource(10000))
                using (var listener = tester.ExplorerClient.CreateWebsocketNotificationSession())
                {
                    listener.ListenAllDerivationSchemes();
                    var replaced = tester.ExplorerNode.SignRawTransaction(tx);
                    Thread.Sleep(1000); // Make sure the replacement has a different timestamp
                    var tx2 = tester.ExplorerNode.SendRawTransaction(replaced);
                    Logs.Tester.LogInformation(
                        $"Let's RBF with a payment of {payment2} ({tx2}), waiting for NBXplorer to pick it up");
                    Assert.Equal(tx2,
                        ((NewTransactionEvent)listener.NextEvent(cts.Token)).TransactionData.TransactionHash);
                }

                Logs.Tester.LogInformation($"The invoice should now not be paidOver anymore");
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(payment2, invoice.BtcPaid);
                    Assert.Equal("False", invoice.ExceptionStatus.ToString());
                });


                Logs.Tester.LogInformation(
                    $"Let's test out rbf payments where the payment gets sent elsehwere instead");
                var invoice2 =
                    user.BitPay.CreateInvoice(new Invoice() { Price = 0.01m, Currency = "BTC" }, Facade.Merchant);

                var invoice2Address =
                    BitcoinAddress.Create(invoice2.BitcoinAddress, user.SupportedNetwork.NBitcoinNetwork);
                uint256 invoice2tx1Id =
                    await tester.ExplorerNode.SendToAddressAsync(invoice2Address, invoice2.BtcDue, replaceable: true);
                Transaction invoice2Tx1 = null;
                TestUtils.Eventually(() =>
                {
                    invoice2 = user.BitPay.GetInvoice(invoice2.Id);
                    Assert.Equal("paid", invoice2.Status);
                    invoice2Tx1 = tester.ExplorerNode.GetRawTransaction(new uint256(invoice2tx1Id));
                });
                var invoice2Tx2 = invoice2Tx1.Clone();
                foreach (var input in invoice2Tx2.Inputs)
                {
                    input.ScriptSig = Script.Empty; //Strip signatures
                    input.WitScript = WitScript.Empty; //Strip signatures
                }

                output = invoice2Tx2.Outputs.First(o =>
                    o.ScriptPubKey == invoice2Address.ScriptPubKey);
                output.Value -= new Money(10_000, MoneyUnit.Satoshi);
                output.ScriptPubKey = new Key().ScriptPubKey;
                invoice2Tx2 = await tester.ExplorerNode.SignRawTransactionAsync(invoice2Tx2);
                await tester.ExplorerNode.SendRawTransactionAsync(invoice2Tx2);
                tester.ExplorerNode.Generate(1);
                await TestUtils.EventuallyAsync(async () =>
                {
                    var i = await tester.PayTester.InvoiceRepository.GetInvoice(invoice2.Id);
                    Assert.Equal(InvoiceStatusLegacy.New, i.Status);
                    Assert.Single(i.GetPayments());
                    Assert.False(i.GetPayments().First().Accounted);
                });

                Logs.Tester.LogInformation(
                    $"Let's test if we can RBF a normal payment without adding fees to the invoice");
                user.SetNetworkFeeMode(NetworkFeeMode.MultiplePaymentsOnly);
                invoice = user.BitPay.CreateInvoice(new Invoice() { Price = 5000.0m, Currency = "USD" }, Facade.Merchant);
                payment1 = invoice.BtcDue;
                tx1 = new uint256(tester.ExplorerNode.SendCommand("sendtoaddress", new object[]
                {
                    invoice.BitcoinAddress, payment1.ToString(), null, //comment
                    null, //comment_to
                    false, //subtractfeefromamount
                    true, //replaceable
                }).ResultString);
                Logs.Tester.LogInformation($"Paid {tx1}");
                TestUtils.Eventually(() =>
                    {
                        invoice = user.BitPay.GetInvoice(invoice.Id);
                        Assert.Equal(payment1, invoice.BtcPaid);
                        Assert.Equal("paid", invoice.Status);
                        Assert.Equal("False", invoice.ExceptionStatus.ToString());
                    }
                );
                var tx1Bump = new uint256(tester.ExplorerNode.SendCommand("bumpfee", new object[]
                {
                    tx1.ToString(),
                }).Result["txid"].Value<string>());
                Logs.Tester.LogInformation($"Bumped with {tx1Bump}");
                await TestUtils.EventuallyAsync(async () =>
                    {
                        var invoiceEntity = await tester.PayTester.InvoiceRepository.GetInvoice(invoice.Id);
                        var btcPayments = invoiceEntity.GetAllBitcoinPaymentData().ToArray();
                        var payments = invoiceEntity.GetPayments().ToArray();
                        Assert.Equal(tx1, btcPayments[0].Outpoint.Hash);
                        Assert.False(payments[0].Accounted);
                        Assert.Equal(tx1Bump, payments[1].Outpoint.Hash);
                        Assert.True(payments[1].Accounted);
                        Assert.Equal(0.0m, payments[1].NetworkFee);
                        invoice = user.BitPay.GetInvoice(invoice.Id);
                        Assert.Equal(payment1, invoice.BtcPaid);
                        Assert.Equal("paid", invoice.Status);
                        Assert.Equal("False", invoice.ExceptionStatus.ToString());
                    }
                );
            }
        }

        // [Fact(Timeout = TestTimeout)]
        [Fact()]
        [Trait("Integration", "Integration")]
        public async Task CanSaveKeyPathForOnChainPayments()
        {
            using var tester = ServerTester.Create();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            await user.RegisterDerivationSchemeAsync("BTC");

            var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(0.01m, "BTC"));
            await tester.WaitForEvent<InvoiceEvent>(async () =>
            {
                var tx = await tester.ExplorerNode.SendToAddressAsync(
                    BitcoinAddress.Create(invoice.BitcoinAddress, Network.RegTest),
                    Money.Coins(0.01m));
            });



            var payments = Assert.IsType<InvoiceDetailsModel>(
                    Assert.IsType<ViewResult>(await user.GetController<InvoiceController>().Invoice(invoice.Id)).Model)
                .Payments;
            Assert.Single(payments);
            var paymentData = payments.First().GetCryptoPaymentData() as BitcoinLikePaymentData;
            Assert.NotNull(paymentData.KeyPath);
        }

        [Fact(Timeout = TestTimeout)]
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

        [Fact(Timeout = TestTimeout)]
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

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async void CheckCORSSetOnBitpayAPI()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                foreach (var req in new[] { "invoices/", "invoices", "rates", "tokens" }.Select(async path =>
                  {
                      using (HttpClient client = new HttpClient())
                      {
                          HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Options,
                              tester.PayTester.ServerUri.AbsoluteUri + path);
                          message.Headers.Add("Access-Control-Request-Headers", "test");
                          var response = await client.SendAsync(message);
                          response.EnsureSuccessStatusCode();
                          Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var val));
                          Assert.Equal("*", val.FirstOrDefault());
                          Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Headers", out val));
                          Assert.Equal("test", val.FirstOrDefault());
                      }
                  }).ToList())
                {
                    await req;
                }

                HttpClient client2 = new HttpClient();
                HttpRequestMessage message2 = new HttpRequestMessage(HttpMethod.Options,
                    tester.PayTester.ServerUri.AbsoluteUri + "rates");
                var response2 = await client2.SendAsync(message2);
                Assert.True(response2.Headers.TryGetValues("Access-Control-Allow-Origin", out var val2));
                Assert.Equal("*", val2.FirstOrDefault());
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task TestAccessBitpayAPI()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                Assert.False(user.BitPay.TestAccess(Facade.Merchant));
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                Assert.True(user.BitPay.TestAccess(Facade.Merchant));

                // Test request pairing code client side
                var storeController = user.GetController<StoresController>();
                storeController
                    .CreateToken(user.StoreId, new CreateTokenViewModel() { Label = "test2", StoreId = user.StoreId })
                    .GetAwaiter().GetResult();
                Assert.NotNull(storeController.GeneratedPairingCode);


                var k = new Key();
                var bitpay = new Bitpay(k, tester.PayTester.ServerUri);
                bitpay.AuthorizeClient(new PairingCode(storeController.GeneratedPairingCode)).Wait();
                Assert.True(bitpay.TestAccess(Facade.Merchant));
                Assert.True(bitpay.TestAccess(Facade.PointOfSale));
                // Same with new instance
                bitpay = new Bitpay(k, tester.PayTester.ServerUri);
                Assert.True(bitpay.TestAccess(Facade.Merchant));
                Assert.True(bitpay.TestAccess(Facade.PointOfSale));

                // Can generate API Key
                var repo = tester.PayTester.GetService<TokenRepository>();
                Assert.Empty(repo.GetLegacyAPIKeys(user.StoreId).GetAwaiter().GetResult());
                Assert.IsType<RedirectToActionResult>(user.GetController<StoresController>()
                    .GenerateAPIKey(user.StoreId).GetAwaiter().GetResult());

                var apiKey = Assert.Single(repo.GetLegacyAPIKeys(user.StoreId).GetAwaiter().GetResult());
                ///////

                // Generating a new one remove the previous
                Assert.IsType<RedirectToActionResult>(user.GetController<StoresController>()
                    .GenerateAPIKey(user.StoreId).GetAwaiter().GetResult());
                var apiKey2 = Assert.Single(repo.GetLegacyAPIKeys(user.StoreId).GetAwaiter().GetResult());
                Assert.NotEqual(apiKey, apiKey2);
                ////////

                apiKey = apiKey2;

                // Can create an invoice with this new API Key
                HttpClient client = new HttpClient();
                HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post,
                    tester.PayTester.ServerUri.AbsoluteUri + "invoices");
                message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                    Encoders.Base64.EncodeData(Encoders.ASCII.DecodeData(apiKey)));
                var invoice = new Invoice() { Price = 5000.0m, Currency = "USD" };
                message.Content = new StringContent(JsonConvert.SerializeObject(invoice), Encoding.UTF8,
                    "application/json");
                var result = client.SendAsync(message).GetAwaiter().GetResult();
                result.EnsureSuccessStatusCode();
                /////////////////////

                // Have error 403 with bad signature
                client = new HttpClient();
                HttpRequestMessage mess =
                    new HttpRequestMessage(HttpMethod.Get, tester.PayTester.ServerUri.AbsoluteUri + "tokens");
                mess.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
                mess.Headers.Add("x-signature",
                    "3045022100caa123193afc22ef93d9c6b358debce6897c09dd9869fe6fe029c9cb43623fac022000b90c65c50ba8bbbc6ebee8878abe5659e17b9f2e1b27d95eda4423da5608fe");
                mess.Headers.Add("x-identity",
                    "04b4d82095947262dd70f94c0a0e005ec3916e3f5f2181c176b8b22a52db22a8c436c4703f43a9e8884104854a11e1eb30df8fdf116e283807a1f1b8fe4c182b99");
                mess.Method = HttpMethod.Get;
                result = client.SendAsync(mess).GetAwaiter().GetResult();
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized, result.StatusCode);
                //
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseExchangeSpecificRate()
        {
            using (var tester = ServerTester.Create())
            {
                tester.PayTester.MockRates = false;
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
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
        }

        private static async Task<decimal> CreateInvoice(ServerTester tester, TestAccount user, string exchange,
            string currency = "USD")
        {
            var storeController = user.GetController<StoresController>();
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

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseAnyoneCanCreateInvoice()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                Logs.Tester.LogInformation("StoreId without anyone can create invoice = 403");
                var response = await tester.PayTester.HttpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, $"invoices?storeId={user.StoreId}")
                    {
                        Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8,
                            "application/json"),
                    });
                Assert.Equal(403, (int)response.StatusCode);

                Logs.Tester.LogInformation(
                    "No store without  anyone can create invoice = 404 because the bitpay API can't know the storeid");
                response = await tester.PayTester.HttpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, $"invoices")
                    {
                        Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8,
                            "application/json"),
                    });
                Assert.Equal(404, (int)response.StatusCode);

                user.ModifyStore(s => s.AnyoneCanCreateInvoice = true);

                Logs.Tester.LogInformation("Bad store with anyone can create invoice = 403");
                response = await tester.PayTester.HttpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, $"invoices?storeId=badid")
                    {
                        Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8,
                            "application/json"),
                    });
                Assert.Equal(403, (int)response.StatusCode);

                Logs.Tester.LogInformation("Good store with anyone can create invoice = 200");
                response = await tester.PayTester.HttpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Post, $"invoices?storeId={user.StoreId}")
                    {
                        Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8,
                            "application/json"),
                    });
                Assert.Equal(200, (int)response.StatusCode);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanTweakRate()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                // First we try payment with a merchant having only BTC
                var invoice1 = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 5000.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);
                Assert.Equal(Money.Coins(1.0m), invoice1.BtcPrice);

                var storeController = user.GetController<StoresController>();
                var vm = (RatesViewModel)((ViewResult)storeController.Rates()).Model;
                Assert.Equal(0.0, vm.Spread);
                vm.Spread = 40;
                await storeController.Rates(vm);


                var invoice2 = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 5000.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                var expectedRate = 5000.0m * 0.6m;
                var expectedCoins = invoice2.Price / expectedRate;
                Assert.True(invoice2.BtcPrice.Almost(Money.Coins(expectedCoins), 0.00001m));
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanModifyRates()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var store = user.GetController<StoresController>();
                var rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.False(rateVm.ShowScripting);
                Assert.Equal(CoinGeckoRateProvider.CoinGeckoName, rateVm.PreferredExchange);
                Assert.Equal(0.0, rateVm.Spread);
                Assert.Null(rateVm.TestRateRules);

                rateVm.PreferredExchange = "bitflyer";
                Assert.IsType<RedirectToActionResult>(await store.Rates(rateVm, "Save"));
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal("bitflyer", rateVm.PreferredExchange);

                rateVm.ScriptTest = "BTC_JPY,BTC_CAD";
                rateVm.Spread = 10;
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(await store.Rates(rateVm, "Test"))
                    .Model);
                Assert.NotNull(rateVm.TestRateRules);
                Assert.Equal(2, rateVm.TestRateRules.Count);
                Assert.False(rateVm.TestRateRules[0].Error);
                Assert.StartsWith("(bitflyer(BTC_JPY)) * (0.9, 1.1) =", rateVm.TestRateRules[0].Rule,
                    StringComparison.OrdinalIgnoreCase);
                Assert.True(rateVm.TestRateRules[1].Error);
                Assert.IsType<RedirectToActionResult>(await store.Rates(rateVm, "Save"));

                Assert.IsType<RedirectToActionResult>(store.ShowRateRulesPost(true).Result);
                Assert.IsType<RedirectToActionResult>(await store.Rates(rateVm, "Save"));
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal(rateVm.StoreId, user.StoreId);
                Assert.Equal(rateVm.DefaultScript, rateVm.Script);
                Assert.True(rateVm.ShowScripting);
                rateVm.ScriptTest = "BTC_JPY";
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(await store.Rates(rateVm, "Test"))
                    .Model);
                Assert.True(rateVm.ShowScripting);
                Assert.Contains("(bitflyer(BTC_JPY)) * (0.9, 1.1) = ", rateVm.TestRateRules[0].Rule,
                    StringComparison.OrdinalIgnoreCase);

                rateVm.ScriptTest = "BTC_USD,BTC_CAD,DOGE_USD,DOGE_CAD";
                rateVm.Script = "DOGE_X = bittrex(DOGE_BTC) * BTC_X;\n" +
                                "X_CAD = ndax(X_CAD);\n" +
                                "X_X = coingecko(X_X);";
                rateVm.Spread = 50;
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(await store.Rates(rateVm, "Test"))
                    .Model);
                Assert.True(rateVm.TestRateRules.All(t => !t.Error));
                Assert.IsType<RedirectToActionResult>(await store.Rates(rateVm, "Save"));
                store = user.GetController<StoresController>();
                rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
                Assert.Equal(50, rateVm.Spread);
                Assert.True(rateVm.ShowScripting);
                Assert.Contains("DOGE_X", rateVm.Script, StringComparison.OrdinalIgnoreCase);
            }
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
        [Trait("Integration", "Integration")]
        public async Task CanSetPaymentMethodLimits()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                var vm = Assert.IsType<CheckoutExperienceViewModel>(Assert
                    .IsType<ViewResult>(user.GetController<StoresController>().CheckoutExperience()).Model);
                Assert.Single(vm.PaymentMethodCriteria);
                var criteria = vm.PaymentMethodCriteria.First();
                Assert.Equal(new PaymentMethodId("BTC", BitcoinPaymentType.Instance).ToString(), criteria.PaymentMethod);
                criteria.Value = "5 USD";
                criteria.Type = PaymentMethodCriteriaViewModel.CriteriaType.GreaterThan;
                Assert.IsType<RedirectToActionResult>(user.GetController<StoresController>().CheckoutExperience(vm)
                    .Result);

                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 5.5m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                Assert.Single(invoice.CryptoInfo);
                Assert.Equal(PaymentTypes.BTCLike.ToString(), invoice.CryptoInfo[0].PaymentType);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanSetUnifiedQrCode()
        {
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLightning();
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess(true);
                user.RegisterDerivationScheme("BTC", ScriptPubKeyType.Segwit);
                user.RegisterLightningNode("BTC", LightningConnectionType.CLightning);

                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 5.5m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                // validate that invoice data model doesn't have lightning string initially
                var res = await user.GetController<InvoiceController>().Checkout(invoice.Id);
                var paymentMethodFirst = Assert.IsType<PaymentModel>(
                    Assert.IsType<ViewResult>(res).Model
                );
                Assert.DoesNotContain("&lightning=", paymentMethodFirst.InvoiceBitcoinUrlQR);

                // enable unified QR code in settings
                var vm = Assert.IsType<CheckoutExperienceViewModel>(Assert
                    .IsType<ViewResult>(user.GetController<StoresController>().CheckoutExperience()).Model
                );
                vm.OnChainWithLnInvoiceFallback = true;
                Assert.IsType<RedirectToActionResult>(
                    user.GetController<StoresController>().CheckoutExperience(vm).Result
                );

                // validate that QR code now has both onchain and offchain payment urls
                res = await user.GetController<InvoiceController>().Checkout(invoice.Id);
                var paymentMethodSecond = Assert.IsType<PaymentModel>(
                    Assert.IsType<ViewResult>(res).Model
                );
                Assert.Contains("&lightning=", paymentMethodSecond.InvoiceBitcoinUrlQR);
                Assert.StartsWith("bitcoin:", paymentMethodSecond.InvoiceBitcoinUrlQR);
                var split = paymentMethodSecond.InvoiceBitcoinUrlQR.Split('?')[0];

                // Standard for all uppercase characters in QR codes is still not implemented in all wallets
                // But we're proceeding with BECH32 being uppercase
                Assert.True($"bitcoin:{paymentMethodSecond.BtcAddress.ToUpperInvariant()}" == split);
            }
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSetPaymentMethodLimitsLightning()
        {
            using (var tester = ServerTester.Create())
            {
                tester.ActivateLightning();
                await tester.StartAsync();
                await tester.EnsureChannelsSetup();
                var user = tester.NewAccount();
                user.GrantAccess(true);
                user.RegisterLightningNode("BTC", LightningConnectionType.Charge);
                var vm = Assert.IsType<CheckoutExperienceViewModel>(Assert
                    .IsType<ViewResult>(user.GetController<StoresController>().CheckoutExperience()).Model);
                Assert.Single(vm.PaymentMethodCriteria);
                var criteria = vm.PaymentMethodCriteria.First();
                Assert.Equal(new PaymentMethodId("BTC", LightningPaymentType.Instance).ToString(), criteria.PaymentMethod);
                criteria.Value = "2 USD";
                criteria.Type = PaymentMethodCriteriaViewModel.CriteriaType.LessThan;
                Assert.IsType<RedirectToActionResult>(user.GetController<StoresController>().CheckoutExperience(vm)
                    .Result);

                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 1.5m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                Assert.Single(invoice.CryptoInfo);
                Assert.Equal(PaymentTypes.LightningLike.ToString(), invoice.CryptoInfo[0].PaymentType);
            }
        }

        [Fact]
        [Trait("Fast", "Fast")]
        public async Task CanScheduleBackgroundTasks()
        {
            BackgroundJobClient client = new BackgroundJobClient();
            MockDelay mockDelay = new MockDelay();
            client.Delay = mockDelay;
            bool[] jobs = new bool[4];
            Logs.Tester.LogInformation("Start Job[0] in 5 sec");
            client.Schedule((_) =>
            {
                Logs.Tester.LogInformation("Job[0]");
                jobs[0] = true;
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(5.0));
            Logs.Tester.LogInformation("Start Job[1] in 2 sec");
            client.Schedule((_) =>
            {
                Logs.Tester.LogInformation("Job[1]");
                jobs[1] = true;
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(2.0));
            Logs.Tester.LogInformation("Start Job[2] fails in 6 sec");
            client.Schedule((_) =>
            {
                jobs[2] = true;
                throw new Exception("Job[2]");
            }, TimeSpan.FromSeconds(6.0));
            Logs.Tester.LogInformation("Start Job[3] starts in in 7 sec");
            client.Schedule((_) =>
            {
                Logs.Tester.LogInformation("Job[3]");
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
            Logs.Tester.LogInformation("This job will be cancelled");
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

        [Fact(Timeout = TestTimeout)]
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
                    }
                };

            testCases.ForEach(tuple =>
            {
                Assert.Equal(tuple.expectedOutput, InvoiceController.PosDataParser.ParsePosData(tuple.input));
            });
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task PosDataParser_ParsesCorrectly_Slower()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var controller = tester.PayTester.GetController<InvoiceController>(null);

                var testCases =
                    new List<(string input, Dictionary<string, object> expectedOutput)>()
                    {
                        {(null, new Dictionary<string, object>())},
                        {("", new Dictionary<string, object>())},
                        {("{}", new Dictionary<string, object>())},
                        {
                            ("non-json-content",
                                new Dictionary<string, object>() {{string.Empty, "non-json-content"}})
                        },
                        {("[1,2,3]", new Dictionary<string, object>() {{string.Empty, "[1,2,3]"}})},
                        {("{ \"key\": \"value\"}", new Dictionary<string, object>() {{"key", "value"}})},
                        {("{ \"key\": true}", new Dictionary<string, object>() {{"key", "True"}})},
                        {
                            ("{ invalidjson file here}",
                                new Dictionary<string, object>() {{String.Empty, "{ invalidjson file here}"}})
                        }
                    };

                var tasks = new List<Task>();
                foreach (var valueTuple in testCases)
                {
                    tasks.Add(user.BitPay.CreateInvoiceAsync(new Invoice(1, "BTC") { PosData = valueTuple.input })
                        .ContinueWith(async task =>
                        {
                            var result = await controller.Invoice(task.Result.Id);
                            var viewModel =
                                Assert.IsType<InvoiceDetailsModel>(
                                    Assert.IsType<ViewResult>(result).Model);
                            Assert.Equal(valueTuple.expectedOutput, viewModel.PosData);
                        }));
                }

                await Task.WhenAll(tasks);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanExportInvoicesJson()
        {
            decimal GetFieldValue(string input, string fieldName)
            {
                var match = Regex.Match(input, $"\"{fieldName}\":([^,]*)");
                Assert.True(match.Success);
                return decimal.Parse(match.Groups[1].Value.Trim(), CultureInfo.InvariantCulture);
            }

            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                user.SetNetworkFeeMode(NetworkFeeMode.Always);
                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 10,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some \", description",
                        FullNotifications = true
                    }, Facade.Merchant);

                var networkFee = new FeeRate(invoice.MinerFees["BTC"].SatoshiPerBytes).GetFee(100);
                // ensure 0 invoices exported because there are no payments yet
                var jsonResult = user.GetController<InvoiceController>().Export("json").GetAwaiter().GetResult();
                var result = Assert.IsType<ContentResult>(jsonResult);
                Assert.Equal("application/json", result.ContentType);
                Assert.Equal("[]", result.Content);

                var cashCow = tester.ExplorerNode;
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, cashCow.Network);
                //
                var firstPayment = invoice.CryptoInfo[0].TotalDue - 3 * networkFee;
                cashCow.SendToAddress(invoiceAddress, firstPayment);
                Thread.Sleep(1000); // prevent race conditions, ordering payments
                // look if you can reduce thread sleep, this was min value for me

                // should reduce invoice due by 0 USD because payment = network fee
                cashCow.SendToAddress(invoiceAddress, networkFee);
                Thread.Sleep(1000);

                // pay remaining amount
                cashCow.SendToAddress(invoiceAddress, 4 * networkFee);
                Thread.Sleep(1000);

                TestUtils.Eventually(() =>
                {
                    var jsonResultPaid =
                        user.GetController<InvoiceController>().Export("json").GetAwaiter().GetResult();
                    var paidresult = Assert.IsType<ContentResult>(jsonResultPaid);
                    Assert.Equal("application/json", paidresult.ContentType);

                    var parsedJson = JsonConvert.DeserializeObject<object[]>(paidresult.Content);
                    Assert.Equal(3, parsedJson.Length);

                    var invoiceDueAfterFirstPayment = (3 * networkFee).ToDecimal(MoneyUnit.BTC) * invoice.Rate;
                    var pay1str = parsedJson[0].ToString();
                    Assert.Contains("\"InvoiceItemDesc\": \"Some \\\", description\"", pay1str);
                    Assert.Equal(invoiceDueAfterFirstPayment, GetFieldValue(pay1str, "InvoiceDue"));
                    Assert.Contains("\"InvoicePrice\": 10.0", pay1str);
                    Assert.Contains("\"ConversionRate\": 5000.0", pay1str);
                    Assert.Contains($"\"InvoiceId\": \"{invoice.Id}\",", pay1str);

                    var pay2str = parsedJson[1].ToString();
                    Assert.Equal(invoiceDueAfterFirstPayment, GetFieldValue(pay2str, "InvoiceDue"));

                    var pay3str = parsedJson[2].ToString();
                    Assert.Contains("\"InvoiceDue\": 0", pay3str);
                });
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanChangeNetworkFeeMode()
        {
            using (var tester = ServerTester.Create())
            {
                var btc = new PaymentMethodId("BTC", PaymentTypes.BTCLike);
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                foreach (var networkFeeMode in Enum.GetValues(typeof(NetworkFeeMode)).Cast<NetworkFeeMode>())
                {
                    Logs.Tester.LogInformation($"Trying with {nameof(networkFeeMode)}={networkFeeMode}");
                    user.SetNetworkFeeMode(networkFeeMode);
                    var invoice = user.BitPay.CreateInvoice(
                        new Invoice()
                        {
                            Price = 10,
                            Currency = "USD",
                            PosData = "posData",
                            OrderId = "orderId",
                            ItemDesc = "Some \", description",
                            FullNotifications = true
                        }, Facade.Merchant);
                    var nextNetworkFee = (await tester.PayTester.InvoiceRepository.GetInvoice(invoice.Id))
                        .GetPaymentMethods()[btc]
                        .GetPaymentMethodDetails()
                        .AssertType<BitcoinLikeOnChainPaymentMethod>()
                        .GetNextNetworkFee();
                    var firstPaymentFee = nextNetworkFee;
                    switch (networkFeeMode)
                    {
                        case NetworkFeeMode.Never:
                        case NetworkFeeMode.MultiplePaymentsOnly:
                            Assert.Equal(0.0m, nextNetworkFee);
                            break;
                        case NetworkFeeMode.Always:
                            Assert.NotEqual(0.0m, nextNetworkFee);
                            break;
                    }

                    var missingMoney = Money.Satoshis(5000).ToDecimal(MoneyUnit.BTC);
                    var cashCow = tester.ExplorerNode;
                    var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, cashCow.Network);

                    var due = Money.Parse(invoice.CryptoInfo[0].Due);
                    var productPartDue = (invoice.Price / invoice.Rate);
                    Logs.Tester.LogInformation(
                        $"Product part due is {productPartDue} and due {due} with network fee {nextNetworkFee}");
                    Assert.Equal(productPartDue + nextNetworkFee, due.ToDecimal(MoneyUnit.BTC));
                    var firstPayment = productPartDue - missingMoney;
                    cashCow.SendToAddress(invoiceAddress, Money.Coins(firstPayment));

                    await TestUtils.EventuallyAsync(async () =>
                    {
                        invoice = user.BitPay.GetInvoice(invoice.Id);
                        due = Money.Parse(invoice.CryptoInfo[0].Due);
                        Logs.Tester.LogInformation($"Remaining due after first payment: {due}");
                        Assert.Equal(Money.Coins(firstPayment), Money.Parse(invoice.CryptoInfo[0].Paid));
                        nextNetworkFee = (await tester.PayTester.InvoiceRepository.GetInvoice(invoice.Id))
                            .GetPaymentMethods()[btc]
                            .GetPaymentMethodDetails()
                            .AssertType<BitcoinLikeOnChainPaymentMethod>()
                            .GetNextNetworkFee();
                        switch (networkFeeMode)
                        {
                            case NetworkFeeMode.Never:
                                Assert.Equal(0.0m, nextNetworkFee);
                                break;
                            case NetworkFeeMode.MultiplePaymentsOnly:
                            case NetworkFeeMode.Always:
                                Assert.NotEqual(0.0m, nextNetworkFee);
                                break;
                        }

                        Assert.Equal(missingMoney + firstPaymentFee + nextNetworkFee, due.ToDecimal(MoneyUnit.BTC));
                        Assert.Equal(firstPayment + missingMoney + firstPaymentFee + nextNetworkFee,
                            Money.Parse(invoice.CryptoInfo[0].TotalDue).ToDecimal(MoneyUnit.BTC));
                    });
                    cashCow.SendToAddress(invoiceAddress, due);
                    Logs.Tester.LogInformation($"After payment of {due}, the invoice should be paid");
                    TestUtils.Eventually(() =>
                    {
                        invoice = user.BitPay.GetInvoice(invoice.Id);
                        Assert.Equal("paid", invoice.Status);
                    });
                }
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanExportInvoicesCsv()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                user.SetNetworkFeeMode(NetworkFeeMode.Always);
                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 500,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some \", description",
                        FullNotifications = true
                    }, Facade.Merchant);

                var cashCow = tester.ExplorerNode;
                var invoiceAddress = BitcoinAddress.Create(invoice.CryptoInfo[0].Address, cashCow.Network);
                var firstPayment = invoice.CryptoInfo[0].TotalDue - Money.Coins(0.001m);
                cashCow.SendToAddress(invoiceAddress, firstPayment);
                TestUtils.Eventually(() =>
                {
                    var exportResultPaid =
                        user.GetController<InvoiceController>().Export("csv").GetAwaiter().GetResult();
                    var paidresult = Assert.IsType<ContentResult>(exportResultPaid);
                    Assert.Equal("application/csv", paidresult.ContentType);
                    Assert.Contains($",orderId,{invoice.Id},", paidresult.Content);
                    Assert.Contains($",On-Chain,BTC,0.0991,0.0001,5000.0", paidresult.Content);
                    Assert.Contains($",USD,5.00", paidresult.Content); // Seems hacky but some plateform does not render this decimal the same
                    Assert.Contains("0,,\"Some \"\", description\",new (paidPartial),new,paidPartial",
                        paidresult.Content);
                });
            }
        }


        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCreateAndDeleteApps()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                var user2 = tester.NewAccount();
                user2.GrantAccess();
                var apps = user.GetController<AppsController>();
                var apps2 = user2.GetController<AppsController>();
                var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp().Result).Model);
                Assert.NotNull(vm.SelectedAppType);
                Assert.Null(vm.Name);
                vm.Name = "test";
                vm.SelectedAppType = AppType.PointOfSale.ToString();
                var redirectToAction = Assert.IsType<RedirectToActionResult>(apps.CreateApp(vm).Result);
                Assert.Equal(nameof(apps.UpdatePointOfSale), redirectToAction.ActionName);
                var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model);
                var appList2 =
                    Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps2.ListApps().Result).Model);
                Assert.Single(appList.Apps);
                Assert.Empty(appList2.Apps);
                Assert.Equal("test", appList.Apps[0].AppName);
                Assert.Equal(apps.CreatedAppId, appList.Apps[0].Id);
                Assert.True(appList.Apps[0].IsOwner);
                Assert.Equal(user.StoreId, appList.Apps[0].StoreId);
                Assert.IsType<NotFoundResult>(apps2.DeleteApp(appList.Apps[0].Id).Result);
                Assert.IsType<ViewResult>(apps.DeleteApp(appList.Apps[0].Id).Result);
                redirectToAction = Assert.IsType<RedirectToActionResult>(apps.DeleteAppPost(appList.Apps[0].Id).Result);
                Assert.Equal(nameof(apps.ListApps), redirectToAction.ActionName);
                appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps().Result).Model);
                Assert.Empty(appList.Apps);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCreateStrangeInvoice()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                DateTimeOffset expiration = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(21);
                var invoice1 = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 0.000000012m,
                        Currency = "USD",
                        FullNotifications = true,
                        ExpirationTime = expiration
                    }, Facade.Merchant);
                Assert.Equal(expiration.ToUnixTimeSeconds(), invoice1.ExpirationTime.ToUnixTimeSeconds());
                var invoice2 = user.BitPay.CreateInvoice(new Invoice() { Price = 0.000000019m, Currency = "USD" },
                    Facade.Merchant);
                Assert.Equal(0.000000012m, invoice1.Price);
                Assert.Equal(0.000000019m, invoice2.Price);

                // Should round up to 1 because 0.000000019 is unsignificant
                var invoice3 = user.BitPay.CreateInvoice(
                    new Invoice() { Price = 1.000000019m, Currency = "USD", FullNotifications = true }, Facade.Merchant);
                Assert.Equal(1m, invoice3.Price);

                // Should not round up at 8 digit because the 9th is insignificant
                var invoice4 = user.BitPay.CreateInvoice(
                    new Invoice() { Price = 1.000000019m, Currency = "BTC", FullNotifications = true }, Facade.Merchant);
                Assert.Equal(1.00000002m, invoice4.Price);

                // But not if the 9th is insignificant
                invoice4 = user.BitPay.CreateInvoice(
                    new Invoice() { Price = 0.000000019m, Currency = "BTC", FullNotifications = true }, Facade.Merchant);
                Assert.Equal(0.000000019m, invoice4.Price);

                var invoice = user.BitPay.CreateInvoice(
                    new Invoice() { Price = -0.1m, Currency = "BTC", FullNotifications = true }, Facade.Merchant);
                Assert.Equal(0.0m, invoice.Price);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task InvoiceFlowThroughDifferentStatesCorrectly()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");
                await user.SetupWebhook();
                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 5000.0m,
                        TaxIncluded = 1000.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);
                var repo = tester.PayTester.GetService<InvoiceRepository>();
                var ctx = tester.PayTester.GetService<ApplicationDbContextFactory>().CreateContext();
                Assert.Equal(0, invoice.CryptoInfo[0].TxCount);
                Assert.True(invoice.MinerFees.ContainsKey("BTC"));
                Assert.Contains(invoice.MinerFees["BTC"].SatoshiPerBytes, new[] { 100.0m, 20.0m });
                TestUtils.Eventually(() =>
                {
                    var textSearchResult = tester.PayTester.InvoiceRepository.GetInvoices(new InvoiceQuery()
                    {
                        StoreId = new[] { user.StoreId },
                        TextSearch = invoice.OrderId
                    }).GetAwaiter().GetResult();
                    Assert.Single(textSearchResult);
                    textSearchResult = tester.PayTester.InvoiceRepository.GetInvoices(new InvoiceQuery()
                    {
                        StoreId = new[] { user.StoreId },
                        TextSearch = invoice.Id
                    }).GetAwaiter().GetResult();

                    Assert.Single(textSearchResult);
                });

                invoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                Assert.Equal(1000.0m, invoice.TaxIncluded);
                Assert.Equal(5000.0m, invoice.Price);
                Assert.Equal(Money.Coins(0), invoice.BtcPaid);
                Assert.Equal("new", invoice.Status);
                Assert.False((bool)((JValue)invoice.ExceptionStatus).Value);

                Assert.Single(user.BitPay.GetInvoices(invoice.InvoiceTime.UtcDateTime));
                Assert.Empty(user.BitPay.GetInvoices(invoice.InvoiceTime.UtcDateTime + TimeSpan.FromDays(2)));
                Assert.Single(user.BitPay.GetInvoices(invoice.InvoiceTime.UtcDateTime - TimeSpan.FromDays(5)));
                Assert.Single(user.BitPay.GetInvoices(invoice.InvoiceTime.UtcDateTime - TimeSpan.FromDays(5),
                    invoice.InvoiceTime.DateTime + TimeSpan.FromDays(1.0)));
                Assert.Empty(user.BitPay.GetInvoices(invoice.InvoiceTime.UtcDateTime - TimeSpan.FromDays(5),
                    invoice.InvoiceTime.DateTime - TimeSpan.FromDays(1)));


                var firstPayment = Money.Coins(0.04m);

                var txFee = Money.Zero;

                var cashCow = tester.ExplorerNode;

                var invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);
                Assert.True(IsMapped(invoice, ctx));
                cashCow.SendToAddress(invoiceAddress, firstPayment);

                var invoiceEntity = repo.GetInvoice(invoice.Id, true).GetAwaiter().GetResult();
                Assert.Single(invoiceEntity.HistoricalAddresses);
                Assert.Null(invoiceEntity.HistoricalAddresses[0].UnAssigned);

                Money secondPayment = Money.Zero;

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("new", localInvoice.Status);
                    Assert.Equal(firstPayment, localInvoice.BtcPaid);
                    txFee = localInvoice.BtcDue - invoice.BtcDue;
                    Assert.Equal("paidPartial", localInvoice.ExceptionStatus.ToString());
                    Assert.Equal(1, localInvoice.CryptoInfo[0].TxCount);
                    Assert.NotEqual(localInvoice.BitcoinAddress, invoice.BitcoinAddress); //New address
                    Assert.True(IsMapped(invoice, ctx));
                    Assert.True(IsMapped(localInvoice, ctx));

                    invoiceEntity = repo.GetInvoice(invoice.Id, true).GetAwaiter().GetResult();
                    var historical1 =
                        invoiceEntity.HistoricalAddresses.FirstOrDefault(h => h.GetAddress() == invoice.BitcoinAddress);
                    Assert.NotNull(historical1.UnAssigned);
                    var historical2 =
                        invoiceEntity.HistoricalAddresses.FirstOrDefault(h =>
                            h.GetAddress() == localInvoice.BitcoinAddress);
                    Assert.Null(historical2.UnAssigned);
                    invoiceAddress = BitcoinAddress.Create(localInvoice.BitcoinAddress, cashCow.Network);
                    secondPayment = localInvoice.BtcDue;
                });

                cashCow.SendToAddress(invoiceAddress, secondPayment);

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.Equal(2, localInvoice.CryptoInfo[0].TxCount);
                    Assert.Equal(firstPayment + secondPayment, localInvoice.BtcPaid);
                    Assert.Equal(Money.Zero, localInvoice.BtcDue);
                    Assert.Equal(localInvoice.BitcoinAddress, invoiceAddress.ToString()); //no new address generated
                    Assert.True(IsMapped(localInvoice, ctx));
                    Assert.False((bool)((JValue)localInvoice.ExceptionStatus).Value);
                });

                cashCow.Generate(1); //The user has medium speed settings, so 1 conf is enough to be confirmed

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("confirmed", localInvoice.Status);
                });

                cashCow.Generate(5); //Now should be complete

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("complete", localInvoice.Status);
                    Assert.NotEqual(0.0m, localInvoice.Rate);
                });

                invoice = user.BitPay.CreateInvoice(new Invoice()
                {
                    Price = 5000.0m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    //RedirectURL = redirect + "redirect",
                    //NotificationURL = CallbackUri + "/notification",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);
                invoiceAddress = BitcoinAddress.Create(invoice.BitcoinAddress, cashCow.Network);

                var txId = cashCow.SendToAddress(invoiceAddress, invoice.BtcDue + Money.Coins(1));

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("paid", localInvoice.Status);
                    Assert.Equal(Money.Zero, localInvoice.BtcDue);
                    Assert.Equal("paidOver", (string)((JValue)localInvoice.ExceptionStatus).Value);

                    var textSearchResult = tester.PayTester.InvoiceRepository.GetInvoices(new InvoiceQuery()
                    {
                        StoreId = new[] { user.StoreId },
                        TextSearch = txId.ToString()
                    }).GetAwaiter().GetResult();
                    Assert.Single(textSearchResult);
                });

                cashCow.Generate(1);

                TestUtils.Eventually(() =>
                {
                    var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                    Assert.Equal("confirmed", localInvoice.Status);
                    Assert.Equal(Money.Zero, localInvoice.BtcDue);
                    Assert.Equal("paidOver", (string)((JValue)localInvoice.ExceptionStatus).Value);
                });

                // Test on the webhooks
                user.AssertHasWebhookEvent<WebhookInvoiceSettledEvent>(WebhookEventType.InvoiceSettled,
                    c =>
                    {
                        Assert.False(c.ManuallyMarked);
                    });
                user.AssertHasWebhookEvent<WebhookInvoiceProcessingEvent>(WebhookEventType.InvoiceProcessing,
                    c =>
                    {
                        Assert.True(c.OverPaid);
                    });
                user.AssertHasWebhookEvent<WebhookInvoiceReceivedPaymentEvent>(WebhookEventType.InvoiceReceivedPayment,
                    c =>
                    {
                        Assert.False(c.AfterExpiration);
                    });
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
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
                Logs.Tester.LogInformation($"Testing {result.ExpectedName}");
                if (result.ExpectedName == "ndax")
                {
                    Logs.Tester.LogInformation($"Skipping (currently crashing)");
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
                else
                {
                    // This check if the currency pair is using right currency pair
                    Assert.Contains(exchangeRates.ByExchange[result.ExpectedName],
                        e => (e.CurrencyPair == new CurrencyPair("BTC", "USD") ||
                              e.CurrencyPair == new CurrencyPair("BTC", "EUR") ||
                              e.CurrencyPair == new CurrencyPair("BTC", "USDT") ||
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


        [Fact(Timeout = TestTimeout)]
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


        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
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
                Logs.Tester.LogInformation($"Testing {value.Key.ToString()}");
                Assert.True(rateResult.BidAsk != null, $"Impossible to get the rate {rateResult.EvaluatedRule}");
            }
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

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CheckLogsRoute()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var serverController = user.GetController<ServerController>();
                var vm = Assert.IsType<LogsViewModel>(
                    Assert.IsType<ViewResult>(await serverController.LogsView()).Model);
            }
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

        [Fact(Timeout = TestTimeout)]
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
            foreach(var t in tests)
            {
                Assert.Equal(t.Item2, t.Item1.IsValidFileName());
            }
        }

        [Fact(Timeout = TestTimeout)]
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

        [Fact(Timeout = TestTimeout)]
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

        [Fact(Timeout = TestTimeout)]
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


        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanLoginWithNoSecondaryAuthSystemsOrRequestItWhenAdded()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();

                var accountController = tester.PayTester.GetController<AccountController>();

                //no 2fa or u2f enabled, login should work
                Assert.Equal(nameof(HomeController.Index),
                    Assert.IsType<RedirectToActionResult>(await accountController.Login(new LoginViewModel()
                    {
                        Email = user.RegisterDetails.Email,
                        Password = user.RegisterDetails.Password
                    })).ActionName);

                var manageController = user.GetController<ManageController>();

                //by default no u2f devices available
                Assert.Empty(Assert
                    .IsType<U2FAuthenticationViewModel>(Assert
                        .IsType<ViewResult>(await manageController.U2FAuthentication()).Model).Devices);
                var addRequest =
                    Assert.IsType<AddU2FDeviceViewModel>(Assert
                        .IsType<ViewResult>(manageController.AddU2FDevice("label")).Model);
                //name should match the one provided in beginning
                Assert.Equal("label", addRequest.Name);

                //sending an invalid response model back to server, should error out
                Assert.IsType<RedirectToActionResult>(await manageController.AddU2FDevice(addRequest));
                var statusModel = manageController.TempData.GetStatusMessageModel();
                Assert.Equal(StatusMessageModel.StatusSeverity.Error, statusModel.Severity);

                var contextFactory = tester.PayTester.GetService<ApplicationDbContextFactory>();

                //add a fake u2f device in db directly since emulating a u2f device is hard and annoying
                using (var context = contextFactory.CreateContext())
                {
                    var newDevice = new U2FDevice()
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "fake",
                        Counter = 0,
                        KeyHandle = UTF8Encoding.UTF8.GetBytes("fake"),
                        PublicKey = UTF8Encoding.UTF8.GetBytes("fake"),
                        AttestationCert = UTF8Encoding.UTF8.GetBytes("fake"),
                        ApplicationUserId = user.UserId
                    };
                    await context.U2FDevices.AddAsync(newDevice);
                    await context.SaveChangesAsync();

                    Assert.NotNull(newDevice.Id);
                    Assert.NotEmpty(Assert
                        .IsType<U2FAuthenticationViewModel>(Assert
                            .IsType<ViewResult>(await manageController.U2FAuthentication()).Model).Devices);
                }

                //check if we are showing the u2f login screen now
                var secondLoginResult = Assert.IsType<ViewResult>(await accountController.Login(new LoginViewModel()
                {
                    Email = user.RegisterDetails.Email,
                    Password = user.RegisterDetails.Password
                }));

                Assert.Equal("SecondaryLogin", secondLoginResult.ViewName);
                var vm = Assert.IsType<SecondaryLoginViewModel>(secondLoginResult.Model);
                //2fa was never enabled for user so this should be empty
                Assert.Null(vm.LoginWith2FaViewModel);
                Assert.NotNull(vm.LoginWithU2FViewModel);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async void CheckOnionlocationForNonOnionHtmlRequests()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var url = tester.PayTester.ServerUri.AbsoluteUri;

                // check onion location is present for HTML page request
                using var htmlRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                htmlRequest.Headers.TryAddWithoutValidation("Accept", "text/html,*/*");

                var htmlResponse = await tester.PayTester.HttpClient.SendAsync(htmlRequest);
                htmlResponse.EnsureSuccessStatusCode();
                Assert.True(htmlResponse.Headers.TryGetValues("Onion-Location", out var onionLocation));
                Assert.StartsWith("http://wsaxew3qa5ljfuenfebmaf3m5ykgatct3p6zjrqwoouj3foererde3id.onion", onionLocation.FirstOrDefault() ?? "no-onion-location-header");

                // no onion location for other mime types
                using var otherRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                otherRequest.Headers.TryAddWithoutValidation("Accept", "*/*");

                var otherResponse = await tester.PayTester.HttpClient.SendAsync(otherRequest);
                otherResponse.EnsureSuccessStatusCode();
                Assert.False(otherResponse.Headers.Contains("Onion-Location"));
            }
        }

        private static bool IsMapped(Invoice invoice, ApplicationDbContext ctx)
        {
            var h = BitcoinAddress.Create(invoice.BitcoinAddress, Network.RegTest).ScriptPubKey.Hash.ToString();
            return (ctx.AddressInvoices.Where(i => i.InvoiceDataId == invoice.Id).ToArrayAsync().GetAwaiter()
                    .GetResult())
                .Where(i => i.GetAddress() == h).Any();
        }


        class MockVersionFetcher : IVersionFetcher
        {
            public const string MOCK_NEW_VERSION = "9.9.9.9";
            public Task<string> Fetch(CancellationToken cancellation)
            {
                return Task.FromResult(MOCK_NEW_VERSION);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCheckForNewVersion()
        {
            using (var tester = ServerTester.Create(newDb: true))
            {
                await tester.StartAsync();

                var acc = tester.NewAccount();
                acc.GrantAccess(true);

                var settings = tester.PayTester.GetService<SettingsRepository>();
                await settings.UpdateSetting<PoliciesSettings>(new PoliciesSettings() { CheckForNewVersions = true });

                var mockEnv = tester.PayTester.GetService<BTCPayServerEnvironment>();
                var mockSender = tester.PayTester.GetService<Services.Notifications.NotificationSender>();

                var svc = new NewVersionCheckerHostedService(settings, mockEnv, mockSender, new MockVersionFetcher());
                await svc.ProcessVersionCheck();

                // since last version present in database was null, it should've been updated with version mock returned
                var lastVersion = await settings.GetSettingAsync<NewVersionCheckerDataHolder>();
                Assert.Equal(MockVersionFetcher.MOCK_NEW_VERSION, lastVersion.LastVersion);

                // we should also have notification in UI
                var ctrl = acc.GetController<NotificationsController>();
                var newVersion = MockVersionFetcher.MOCK_NEW_VERSION;

                var vm = Assert.IsType<Models.NotificationViewModels.IndexViewModel>(
                    Assert.IsType<ViewResult>(await ctrl.Index()).Model);

                Assert.True(vm.Skip == 0);
                Assert.True(vm.Count == 50);
                Assert.True(vm.Total == 1);
                Assert.True(vm.Items.Count == 1);

                var fn = vm.Items.First();
                var now = DateTimeOffset.UtcNow;
                Assert.True(fn.Created >= now.AddSeconds(-3));
                Assert.True(fn.Created <= now);
                Assert.Equal($"New version {newVersion} released!", fn.Body);
                Assert.Equal($"https://github.com/btcpayserver/btcpayserver/releases/tag/v{newVersion}", fn.ActionLink);
                Assert.False(fn.Seen);
            }
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanDoLightningInternalNodeMigration()
        {
            using (var tester = ServerTester.Create(newDb: true))
            {
                tester.ActivateLightning(LightningConnectionType.CLightning);
                await tester.StartAsync();
                var acc = tester.NewAccount();
                await acc.GrantAccessAsync(true);
                await acc.CreateStoreAsync();
                
                // Test if legacy DerivationStrategy column is converted to DerivationStrategies
                var store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
                var xpub = "tpubDDmH1briYfZcTDMEc7uMEA5hinzjUTzR9yMC1drxTMeiWyw1VyCqTuzBke6df2sqbfw9QG6wbgTLF5yLjcXsZNaXvJMZLwNEwyvmiFWcLav";
                var derivation = $"{xpub}-[legacy]";
                store.DerivationStrategy = derivation;
                await tester.PayTester.StoreRepository.UpdateStore(store);
                await RestartMigration(tester);
                store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
                Assert.True(string.IsNullOrEmpty(store.DerivationStrategy));
                var v = (DerivationSchemeSettings)store.GetSupportedPaymentMethods(tester.NetworkProvider).First();
                Assert.Equal(derivation, v.AccountDerivation.ToString());
                Assert.Equal(derivation, v.AccountOriginal.ToString());
                Assert.Equal(xpub, v.SigningKey.ToString());
                Assert.Equal(xpub, v.GetSigningAccountKeySettings().AccountKey.ToString());
                
                await acc.RegisterLightningNodeAsync("BTC", LightningConnectionType.CLightning, true);
                store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
                var lnMethod = store.GetSupportedPaymentMethods(tester.NetworkProvider).OfType<LightningSupportedPaymentMethod>().First();
                Assert.NotNull(lnMethod.GetExternalLightningUrl());
                await RestartMigration(tester);

                store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
                lnMethod = store.GetSupportedPaymentMethods(tester.NetworkProvider).OfType<LightningSupportedPaymentMethod>().First();
                Assert.Null(lnMethod.GetExternalLightningUrl());

                // Test if legacy lightning charge settings are converted to LightningConnectionString
                store.DerivationStrategies = new JObject()
                {
                    new JProperty("BTC_LightningLike", new JObject()
                    {
                        new JProperty("LightningChargeUrl", "http://mycharge.com/"),
                        new JProperty("Username", "usr"),
                        new JProperty("Password", "pass"),
                        new JProperty("CryptoCode", "BTC"),
                        new JProperty("PaymentId", "someshit"),
                    })
                }.ToString();
                await tester.PayTester.StoreRepository.UpdateStore(store);
                await RestartMigration(tester);

                store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
                lnMethod = store.GetSupportedPaymentMethods(tester.NetworkProvider).OfType<LightningSupportedPaymentMethod>().First();
                Assert.NotNull(lnMethod.GetExternalLightningUrl());

                var url = lnMethod.GetExternalLightningUrl();
                Assert.Equal(LightningConnectionType.Charge, url.ConnectionType);
                Assert.Equal("pass", url.Password);
                Assert.Equal("usr", url.Username);

                // Test if lightning connection strings get migrated to internal
                store.DerivationStrategies = new JObject()
                {
                    new JProperty("BTC_LightningLike", new JObject()
                    {
                        new JProperty("CryptoCode", "BTC"),
                        new JProperty("LightningConnectionString", tester.PayTester.IntegratedLightning),
                    })
                }.ToString();
                await tester.PayTester.StoreRepository.UpdateStore(store);
                await RestartMigration(tester);
                store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);
                lnMethod = store.GetSupportedPaymentMethods(tester.NetworkProvider).OfType<LightningSupportedPaymentMethod>().First();
                Assert.True(lnMethod.IsInternalNode);
            }
        }


        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanDoInvoiceMigrations()
        {
            using (var tester = ServerTester.Create(newDb: true))
            {
                await tester.StartAsync();

                var acc = tester.NewAccount();
                await acc.GrantAccessAsync(true);
                await acc.CreateStoreAsync();
                await acc.RegisterDerivationSchemeAsync("BTC");
                var store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);

                var blob = store.GetStoreBlob();
                var serializer = new Serializer(null);

                blob.AdditionalData = new Dictionary<string, JToken>();
                blob.AdditionalData.Add("rateRules", JToken.Parse(
                    serializer.ToString(new List<MigrationStartupTask.RateRule_Obsolete>()
                    {
                        new MigrationStartupTask.RateRule_Obsolete()
                        {
                            Multiplier = 2
                        }
                    })));
                blob.AdditionalData.Add("walletKeyPathRoots", JToken.Parse(
                    serializer.ToString(new Dictionary<string, string>()
                    {
                        {
                            new PaymentMethodId("BTC", BitcoinPaymentType.Instance).ToString(),
                            new KeyPath("44'/0'/0'").ToString()
                        }
                    })));

                blob.AdditionalData.Add("networkFeeDisabled", JToken.Parse(
                    serializer.ToString((bool?)true)));

                blob.AdditionalData.Add("onChainMinValue", JToken.Parse(
                    serializer.ToString(new CurrencyValue()
                    {
                        Currency = "USD",
                        Value = 5m
                    }.ToString())));
                blob.AdditionalData.Add("lightningMaxValue", JToken.Parse(
                    serializer.ToString(new CurrencyValue()
                    {
                        Currency = "USD",
                        Value = 5m
                    }.ToString())));

                store.SetStoreBlob(blob);
                await tester.PayTester.StoreRepository.UpdateStore(store);
                await RestartMigration(tester);

                store = await tester.PayTester.StoreRepository.FindStore(acc.StoreId);

                blob = store.GetStoreBlob();
                Assert.Empty(blob.AdditionalData);
                Assert.Single(blob.PaymentMethodCriteria);
                Assert.Contains(blob.PaymentMethodCriteria,
                    criteria => criteria.PaymentMethod == new PaymentMethodId("BTC", BitcoinPaymentType.Instance) &&
                                criteria.Above && criteria.Value.Value == 5m && criteria.Value.Currency == "USD");
                Assert.Equal(NetworkFeeMode.Never, blob.NetworkFeeMode);
                Assert.Contains(store.GetSupportedPaymentMethods(tester.NetworkProvider), method =>
                    method is DerivationSchemeSettings dss &&
                    method.PaymentId == new PaymentMethodId("BTC", BitcoinPaymentType.Instance) &&
                    dss.AccountKeyPath == new KeyPath("44'/0'/0'"));

            }
        }

        private static async Task RestartMigration(ServerTester tester)
        {
            var settings = tester.PayTester.GetService<SettingsRepository>();
            await settings.UpdateSetting<MigrationSettings>(new MigrationSettings());
            var migrationStartupTask = tester.PayTester.GetService<IServiceProvider>().GetServices<IStartupTask>()
                .Single(task => task is MigrationStartupTask);
            await migrationStartupTask.ExecuteAsync();
        }

        [Fact(Timeout = TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task EmailSenderTests()
        {
            using (var tester = ServerTester.Create(newDb: true))
            {
                await tester.StartAsync();

                var acc = tester.NewAccount();
                acc.GrantAccess(true);

                var settings = tester.PayTester.GetService<SettingsRepository>();
                var emailSenderFactory = tester.PayTester.GetService<EmailSenderFactory>();
                
                Assert.Null(await Assert.IsType<ServerEmailSender>(emailSenderFactory.GetEmailSender()).GetEmailSettings());
                Assert.Null(await Assert.IsType<StoreEmailSender>(emailSenderFactory.GetEmailSender(acc.StoreId)).GetEmailSettings());

                
                await settings.UpdateSetting(new PoliciesSettings() { DisableStoresToUseServerEmailSettings = false });
                await settings.UpdateSetting(new EmailSettings()
                {
                 From   = "admin@admin.com",
                 Login = "admin@admin.com",
                 Password = "admin@admin.com",
                 Port = 1234,
                 Server = "admin.com",
                 EnableSSL = true
                });
                Assert.Equal("admin@admin.com",(await Assert.IsType<ServerEmailSender>(emailSenderFactory.GetEmailSender()).GetEmailSettings()).Login);
                Assert.Equal("admin@admin.com",(await Assert.IsType<StoreEmailSender>(emailSenderFactory.GetEmailSender(acc.StoreId)).GetEmailSettings()).Login);

                await settings.UpdateSetting(new PoliciesSettings() { DisableStoresToUseServerEmailSettings = true });
                Assert.Equal("admin@admin.com",(await Assert.IsType<ServerEmailSender>(emailSenderFactory.GetEmailSender()).GetEmailSettings()).Login);
                Assert.Null(await Assert.IsType<StoreEmailSender>(emailSenderFactory.GetEmailSender(acc.StoreId)).GetEmailSettings());

                Assert.IsType<RedirectToActionResult>(await acc.GetController<StoresController>().Emails(acc.StoreId, new EmailsViewModel(new EmailSettings()
                {
                    From   = "store@store.com",
                    Login = "store@store.com",
                    Password = "store@store.com",
                    Port = 1234,
                    Server = "store.com",
                    EnableSSL = true
                }), ""));
                
                Assert.Equal("store@store.com",(await Assert.IsType<StoreEmailSender>(emailSenderFactory.GetEmailSender(acc.StoreId)).GetEmailSettings()).Login);

            }
        }
        
        
    }
}
