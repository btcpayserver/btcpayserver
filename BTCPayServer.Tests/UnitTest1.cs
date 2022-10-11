using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Fido2;
using BTCPayServer.Fido2.Models;
using BTCPayServer.HostedServices;
using BTCPayServer.Hosting;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Models.AccountViewModels;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Models.ManageViewModels;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payments.PayJoin.Sender;
using BTCPayServer.Plugins.PointOfSale.Controllers;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration;
using BTCPayServer.Storage.ViewModels;
using ExchangeSharp;
using Fido2NetLib;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using NBitpayClient;
using NBXplorer;
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
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class UnitTest1 : UnitTestBase
    {
        public const int LongRunningTestTimeout = 60_000; // 60s

        public UnitTest1(ITestOutputHelper helper) : base(helper)
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

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CheckSwaggerIsConformToSchema()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();

            var sresp = Assert
                .IsType<JsonResult>(await tester.PayTester.GetController<UIHomeController>(acc.UserId, acc.StoreId)
                    .Swagger(tester.PayTester.GetService<IEnumerable<ISwaggerProvider>>())).Value.ToJson();
            JObject swagger = JObject.Parse(sresp);
            var schema = JSchema.Parse(File.ReadAllText(TestUtils.GetTestDataFullPath("OpenAPI-Specification-schema.json")));
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

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task EnsureSwaggerPermissionsDocumented()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();

            var description =
                "BTCPay Server supports authenticating and authorizing users through an API Key that is generated by them. Send the API Key as a header value to Authorization with the format: `token {token}`. For a smoother experience, you can generate a url that redirects users to an API key creation screen.\n\n The following permissions are available to the context of the user creating the API Key:\n\n#OTHERPERMISSIONS#\n\nThe following permissions are available if the user is an administrator:\n\n#SERVERPERMISSIONS#\n\nThe following permissions applies to all stores of the user, you can limit to a specific store with the following format: `btcpay.store.cancreateinvoice:6HSHAEU4iYWtjxtyRs9KyPjM9GAQp8kw2T9VWbGG1FnZ`:\n\n#STOREPERMISSIONS#\n\nNote that API Keys only limits permission of a user and can never expand it. If an API Key has the permission `btcpay.server.canmodifyserversettings` but that the user account creating this API Key is not administrator, the API Key will not be able to modify the server settings.\nSome permissions may include other permissions, see [this operation](#operation/permissionsMetadata).\n";

            var storePolicies =
                UIManageController.AddApiKeyViewModel.PermissionValueItem.PermissionDescriptions.Where(pair =>
                    Policies.IsStorePolicy(pair.Key) && !pair.Key.EndsWith(":", StringComparison.InvariantCulture));
            var serverPolicies =
                UIManageController.AddApiKeyViewModel.PermissionValueItem.PermissionDescriptions.Where(pair =>
                    Policies.IsServerPolicy(pair.Key));
            var otherPolicies =
                UIManageController.AddApiKeyViewModel.PermissionValueItem.PermissionDescriptions.Where(pair =>
                    !Policies.IsStorePolicy(pair.Key) && !Policies.IsServerPolicy(pair.Key));

            description = description.Replace("#OTHERPERMISSIONS#",
                    string.Join("\n", otherPolicies.Select(pair => $"* `{pair.Key}`: {pair.Value.Title}")))
                .Replace("#SERVERPERMISSIONS#",
                    string.Join("\n", serverPolicies.Select(pair => $"* `{pair.Key}`: {pair.Value.Title}")))
                .Replace("#STOREPERMISSIONS#",
                    string.Join("\n", storePolicies.Select(pair => $"* `{pair.Key}`: {pair.Value.Title}")));
            TestLogs.LogInformation(description);

            var sresp = Assert
                .IsType<JsonResult>(await tester.PayTester.GetController<UIHomeController>(acc.UserId, acc.StoreId)
                    .Swagger(tester.PayTester.GetService<IEnumerable<ISwaggerProvider>>())).Value.ToJson();

            JObject json = JObject.Parse(sresp);

            Assert.Equal(description, json["components"]["securitySchemes"]["API_Key"]["description"].Value<string>());
        }
        
        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanStoreArbitrarySettingsWithStore()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            var settingsRepo = tester.PayTester.ServiceProvider.GetRequiredService<IStoreRepository>();
            var arbValue = await settingsRepo.GetSettingAsync<string>(user.StoreId,"arbitrary");
            Assert.Null(arbValue);
            await settingsRepo.UpdateSetting(user.StoreId, "arbitrary", "saved");

            arbValue = await settingsRepo.GetSettingAsync<string>(user.StoreId,"arbitrary");
            Assert.Equal("saved", arbValue);

            await settingsRepo.UpdateSetting<TestData>(user.StoreId, "arbitrary", new TestData() { Name = "hello" });
            var arbData = await settingsRepo.GetSettingAsync<TestData>(user.StoreId, "arbitrary");
            Assert.Equal("hello", arbData.Name);

            var client = await user.CreateClient();
            await client.RemoveStore(user.StoreId);
            tester.Stores.Clear();
            arbValue = await settingsRepo.GetSettingAsync<string>(user.StoreId, "arbitrary");
            Assert.Null(arbValue);
        }
        class TestData
        {
            public string Name { get; set; }
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
        [Trait("Integration", "Integration")]
        public async Task CanAcceptInvoiceWithTolerance2()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();
            user.RegisterDerivationScheme("BTC");

            // Set tolerance to 50%
            var stores = user.GetController<UIStoresController>();
            var response = stores.GeneralSettings();
            var vm = Assert.IsType<GeneralSettingsViewModel>(Assert.IsType<ViewResult>(response).Model);
            Assert.Equal(0.0, vm.PaymentTolerance);
            vm.PaymentTolerance = 50.0;
            Assert.IsType<RedirectToActionResult>(stores.GeneralSettings(vm).Result);

            var invoice = await user.BitPay.CreateInvoiceAsync(
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
            await tester.ExplorerNode.SendToAddressAsync(invoiceAddress,
                Money.Satoshis(invoice.BtcDue.Satoshi * 0.75m));

            TestUtils.Eventually(() =>
            {
                var localInvoice = user.BitPay.GetInvoice(invoice.Id, Facade.Merchant);
                Assert.Equal("paid", localInvoice.Status);
            });
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanThrowBitpay404Error()
        {
            using var tester = CreateServerTester();
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

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task EnsureNewLightningInvoiceOnPartialPayment()
        {
            using var tester = CreateServerTester();
            tester.ActivateLightning();
            await tester.StartAsync();
            await tester.EnsureChannelsSetup();
            var user = tester.NewAccount();
            await user.GrantAccessAsync(true);
            await user.RegisterDerivationSchemeAsync("BTC");
            await user.RegisterLightningNodeAsync("BTC", LightningConnectionType.CLightning);
            await user.SetNetworkFeeMode(NetworkFeeMode.Never);
            await user.ModifyWalletSettings(p => p.SpeedPolicy = SpeedPolicy.HighSpeed);
            var invoice = await user.BitPay.CreateInvoiceAsync(new Invoice(0.0001m, "BTC"));
            await tester.WaitForEvent<InvoiceNewPaymentDetailsEvent>(async () =>
            {
                await tester.ExplorerNode.SendToAddressAsync(
                    BitcoinAddress.Create(invoice.BitcoinAddress, Network.RegTest), Money.Coins(0.00005m));
            }, e => e.InvoiceId == invoice.Id && e.PaymentMethodId.PaymentType == LightningPaymentType.Instance);
            await tester.ExplorerNode.GenerateAsync(1);
            Invoice newInvoice = null;
            await TestUtils.EventuallyAsync(async () =>
            {
                await Task.Delay(1000); // wait a bit for payment to process before fetching new invoice
                newInvoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
                var newBolt11 = newInvoice.CryptoInfo.First(o => o.PaymentUrls.BOLT11 != null).PaymentUrls.BOLT11;
                var oldBolt11 = invoice.CryptoInfo.First(o => o.PaymentUrls.BOLT11 != null).PaymentUrls.BOLT11;
                Assert.NotEqual(newBolt11, oldBolt11);
                Assert.Equal(newInvoice.BtcDue.GetValue(),
                    BOLT11PaymentRequest.Parse(newBolt11, Network.RegTest).MinimumAmount.ToDecimal(LightMoneyUnit.BTC));
            }, 40000);

            TestLogs.LogInformation($"Paying invoice {newInvoice.Id} remaining due amount {newInvoice.BtcDue.GetValue()} via lightning");
            var evt = await tester.WaitForEvent<InvoiceDataChangedEvent>(async () =>
            {
                await tester.SendLightningPaymentAsync(newInvoice);
            }, evt => evt.InvoiceId == invoice.Id);

            var fetchedInvoice = await tester.PayTester.InvoiceRepository.GetInvoice(evt.InvoiceId);
            Assert.Contains(fetchedInvoice.Status, new[] { InvoiceStatusLegacy.Complete, InvoiceStatusLegacy.Confirmed });
            Assert.Equal(InvoiceExceptionStatus.None, fetchedInvoice.ExceptionStatus);

            //BTCPay will attempt to cancel previous bolt11 invoices so that there are less weird edge case scenarios
            TestLogs.LogInformation($"Attempting to pay invoice {invoice.Id} original full amount bolt11 invoice");
            var res = await tester.SendLightningPaymentAsync(invoice);
            Assert.Equal(PayResult.Error, res.Result);

            //NOTE: Eclair does not support cancelling invoice so the below test case would make sense for it
            // TestLogs.LogInformation($"Paying invoice {invoice.Id} original full amount bolt11 invoice ");
            // evt = await tester.WaitForEvent<InvoiceDataChangedEvent>(async () =>
            // {
            //     await tester.SendLightningPaymentAsync(invoice);
            // }, evt => evt.InvoiceId == invoice.Id);
            // Assert.Equal(evt.InvoiceId, invoice.Id);
            // fetchedInvoice = await tester.PayTester.InvoiceRepository.GetInvoice(evt.InvoiceId);
            // Assert.Equal(3, fetchedInvoice.Payments.Count);
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSetLightningServer()
        {
            using var tester = CreateServerTester();
            tester.ActivateLightning();
            await tester.StartAsync();
            await tester.EnsureChannelsSetup();
            var user = tester.NewAccount();
            await user.GrantAccessAsync(true);
            var storeController = user.GetController<UIStoresController>();
            var storeResponse = storeController.GeneralSettings();
            Assert.IsType<ViewResult>(storeResponse);
            Assert.IsType<ViewResult>(storeController.SetupLightningNode(user.StoreId, "BTC"));

            storeController.SetupLightningNode(user.StoreId, new LightningNodeViewModel
            {
                ConnectionString = $"type=charge;server={tester.MerchantCharge.Client.Uri.AbsoluteUri};allowinsecure=true",
                SkipPortTest = true // We can't test this as the IP can't be resolved by the test host :(
            }, "test", "BTC").GetAwaiter().GetResult();
            Assert.False(storeController.TempData.ContainsKey(WellKnownTempData.ErrorMessage));
            storeController.TempData.Clear();
            Assert.True(storeController.ModelState.IsValid);

            Assert.IsType<RedirectToActionResult>(storeController.SetupLightningNode(user.StoreId,
                new LightningNodeViewModel
                {
                    ConnectionString = $"type=charge;server={tester.MerchantCharge.Client.Uri.AbsoluteUri};allowinsecure=true"
                }, "save", "BTC").GetAwaiter().GetResult());

            // Make sure old connection string format does not work
            Assert.IsType<RedirectToActionResult>(storeController.SetupLightningNode(user.StoreId,
                new LightningNodeViewModel { ConnectionString = tester.MerchantCharge.Client.Uri.AbsoluteUri },
                "save", "BTC").GetAwaiter().GetResult());

            storeResponse = storeController.LightningSettings(user.StoreId, "BTC");
            var storeVm =
                Assert.IsType<LightningSettingsViewModel>(Assert
                    .IsType<ViewResult>(storeResponse).Model);
            Assert.NotEmpty(storeVm.ConnectionString);
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

            using var tester = CreateServerTester();
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
            TestLogs.LogInformation($"Trying to send Lightning payment to {invoice.Id}");
            await tester.SendLightningPaymentAsync(invoice);
            TestLogs.LogInformation($"Lightning payment to {invoice.Id} is sent");
            await TestUtils.EventuallyAsync(async () =>
            {
                var localInvoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
                Assert.Equal("complete", localInvoice.Status);
                // C-Lightning may overpay for privacy
                Assert.Contains(localInvoice.ExceptionStatus.ToString(), new[] { "False", "paidOver" });
            });
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseServerInitiatedPairingCode()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();
            acc.Register();
            acc.CreateStore();

            var controller = acc.GetController<UIStoresController>();
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

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanSendIPN()
        {
            using var callbackServer = new CustomServer();
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();
            acc.GrantAccess();
            acc.RegisterDerivationScheme("BTC");
            await acc.ModifyWalletSettings(p => p.SpeedPolicy = SpeedPolicy.LowSpeed);
            var invoice = acc.BitPay.CreateInvoice(new Invoice
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
#pragma warning disable CS0618
            BitcoinUrlBuilder url = new BitcoinUrlBuilder(invoice.PaymentUrls.BIP21,
                tester.NetworkProvider.BTC.NBitcoinNetwork);
            bool receivedPayment = false;
            bool paid = false;
            bool confirmed = false;
            bool completed = false;
            while (!completed || !confirmed || !receivedPayment)
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
                            // TODO, we should check that ReceivedPayment is sent after PaidInFull
                            // for now, we can't ensure this because the ReceivedPayment events isn't sent by the
                            // InvoiceWatcher, contrary to all other events
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

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CantPairTwiceWithSamePubkey()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();
            acc.Register();
            acc.CreateStore();
            var store = acc.GetController<UIStoresController>();
            var pairingCode = acc.BitPay.RequestClientAuthorization("test", Facade.Merchant);
            Assert.IsType<RedirectToActionResult>(store.Pair(pairingCode.ToString(), acc.StoreId).GetAwaiter()
                .GetResult());

            pairingCode = acc.BitPay.RequestClientAuthorization("test1", Facade.Merchant);
            acc.CreateStore();
            var store2 = acc.GetController<UIStoresController>();
            await store2.Pair(pairingCode.ToString(), store2.CurrentStore.Id);
            Assert.Contains(nameof(PairingResult.ReusedKey),
                (string)store2.TempData[WellKnownTempData.ErrorMessage], StringComparison.CurrentCultureIgnoreCase);
        }

        [Fact(Timeout = LongRunningTestTimeout * 2)]
        [Trait("Integration", "Integration")]
        public async Task CanUseTorClient()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var httpFactory = tester.PayTester.GetService<IHttpClientFactory>();
            var client = httpFactory.CreateClient(PayjoinServerCommunicator.PayjoinOnionNamedClient);
            Assert.NotNull(client);

            TestLogs.LogInformation("Querying an clearnet site over tor");
            var response = await client.GetAsync("https://check.torproject.org/");
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("You are not using Tor.", result);
            Assert.Contains("Congratulations. This browser is configured to use Tor.", result);

            TestLogs.LogInformation("Querying a tor website");
            response = await client.GetAsync("http://explorerzydxu5ecjrkwceayqybizmpjjznk5izmitf2modhcusuqlid.onion/");
            if (response.IsSuccessStatusCode) // Sometimes the site goes down
            {
                result = await response.Content.ReadAsStringAsync();
                Assert.Contains("Bitcoin", result);
            }

            TestLogs.LogInformation("...twice");
            response = await client.GetAsync("http://explorerzydxu5ecjrkwceayqybizmpjjznk5izmitf2modhcusuqlid.onion/");
            client.Dispose();

            TestLogs.LogInformation("...three times, but with a new httpclient");
            client = httpFactory.CreateClient(PayjoinServerCommunicator.PayjoinOnionNamedClient);
            response = await client.GetAsync("http://explorerzydxu5ecjrkwceayqybizmpjjznk5izmitf2modhcusuqlid.onion/");

            TestLogs.LogInformation("Querying an onion address which can't be found");
            await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://dwoduwoi.onion/"));


            TestLogs.LogInformation("Querying valid onion but unreachable");
            using var cts = new CancellationTokenSource(10_000);
            try
            {
                await client.GetAsync("http://nzwsosflsoquxirwb2zikz6uxr3u5n5u73l33umtdx4hq5mzm5dycuqd.onion/", cts.Token);
            }
            catch (HttpRequestException)
            {

            }
            catch when (cts.Token.IsCancellationRequested) // Ignore timeouts
            {

            }
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanRescanWallet()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();
            acc.GrantAccess();
            acc.RegisterDerivationScheme("BTC", ScriptPubKeyType.Segwit);
            var btcDerivationScheme = acc.DerivationScheme;

            var walletController = acc.GetController<UIWalletsController>();

            var walletId = new WalletId(acc.StoreId, "BTC");
            acc.IsAdmin = true;
            walletController = acc.GetController<UIWalletsController>();

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
            Assert.Contains("test", tx.Tags.Select(l => l.Text));
            Assert.Contains("test2", tx.Tags.Select(l => l.Text));
            Assert.Equal(2, tx.Tags.GroupBy(l => l.Color).Count());

            Assert.IsType<RedirectToActionResult>(
                await walletController.ModifyTransaction(walletId, tx.Id, removelabel: "test2"));

            transactions = Assert.IsType<ListTransactionsViewModel>(Assert
                .IsType<ViewResult>(walletController.WalletTransactions(walletId).Result).Model);
            tx = Assert.Single(transactions.Transactions);

            Assert.Equal("hello", tx.Comment);
            Assert.Contains("test", tx.Tags.Select(l => l.Text));
            Assert.DoesNotContain("test2", tx.Tags.Select(l => l.Text));
            Assert.Single(tx.Tags.GroupBy(l => l.Color));
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanListInvoices()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();
            await acc.GrantAccessAsync();
            acc.RegisterDerivationScheme("BTC");
            // First we try payment with a merchant having only BTC
            var invoice = await acc.BitPay.CreateInvoiceAsync(
                new Invoice
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
            await cashCow.SendToAddressAsync(invoiceAddress, firstPayment);
            TestUtils.Eventually(() =>
            {
                invoice = acc.BitPay.GetInvoice(invoice.Id);
                Assert.Equal(firstPayment, invoice.CryptoInfo[0].Paid);
            });

            AssertSearchInvoice(acc, true, invoice.Id, null);
            AssertSearchInvoice(acc, true, invoice.Id, null, acc.StoreId);
            AssertSearchInvoice(acc, true, invoice.Id, $"storeid:{acc.StoreId}");
            AssertSearchInvoice(acc, false, invoice.Id, "storeid:doesnotexist");
            AssertSearchInvoice(acc, true, invoice.Id, $"{invoice.Id}");
            AssertSearchInvoice(acc, true, invoice.Id, "exceptionstatus:paidPartial");
            AssertSearchInvoice(acc, false, invoice.Id, "exceptionstatus:paidOver");
            AssertSearchInvoice(acc, true, invoice.Id, "unusual:true");
            AssertSearchInvoice(acc, false, invoice.Id, "unusual:false");

            var time = invoice.InvoiceTime;
            AssertSearchInvoice(acc, true, invoice.Id, $"startdate:{time.ToString("yyyy-MM-dd HH:mm:ss")}");
            AssertSearchInvoice(acc, true, invoice.Id, $"enddate:{time.ToStringLowerInvariant()}");
            AssertSearchInvoice(acc, false, invoice.Id,
                $"startdate:{time.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss")}");
            AssertSearchInvoice(acc, false, invoice.Id,
                $"enddate:{time.AddSeconds(-1).ToString("yyyy-MM-dd HH:mm:ss")}");
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanGetRates()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();
            acc.GrantAccess();
            acc.RegisterDerivationScheme("BTC");

            var rateController = acc.GetController<BitpayRateController>();
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

            var store = acc.GetController<UIStoresController>();
            var ratesVM = (RatesViewModel)(Assert.IsType<ViewResult>(store.Rates()).Model);
            ratesVM.DefaultCurrencyPairs = "BTC_USD,LTC_USD";
            await store.Rates(ratesVM);
            store = acc.GetController<UIStoresController>();
            rateController = acc.GetController<BitpayRateController>();
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

        private void AssertSearchInvoice(TestAccount acc, bool expected, string invoiceId, string filter, string storeId = null)
        {
            var result =
                (InvoicesModel)((ViewResult)acc.GetController<UIInvoiceController>(storeId is not null)
                    .ListInvoices(new InvoicesModel { SearchTerm = filter, StoreId = storeId }).Result).Model;
            Assert.Equal(expected, result.Invoices.Any(i => i.InvoiceId == invoiceId));
        }

        // [Fact(Timeout = TestTimeout)]
        [Fact()]
        [Trait("Integration", "Integration")]
        public async Task CanRBFPayment()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            await user.SetNetworkFeeMode(NetworkFeeMode.Always);
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
            TestLogs.LogInformation(
                $"Let's send a first payment of {payment1} for the {invoice.BtcDue} invoice ({tx1})");
            var invoiceAddress =
                BitcoinAddress.Create(invoice.BitcoinAddress, user.SupportedNetwork.NBitcoinNetwork);

            TestLogs.LogInformation($"The invoice should be paidOver");
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
                TestLogs.LogInformation(
                    $"Let's RBF with a payment of {payment2} ({tx2}), waiting for NBXplorer to pick it up");
                Assert.Equal(tx2,
                    ((NewTransactionEvent)listener.NextEvent(cts.Token)).TransactionData.TransactionHash);
            }

            TestLogs.LogInformation($"The invoice should now not be paidOver anymore");
            TestUtils.Eventually(() =>
            {
                invoice = user.BitPay.GetInvoice(invoice.Id);
                Assert.Equal(payment2, invoice.BtcPaid);
                Assert.Equal("False", invoice.ExceptionStatus.ToString());
            });


            TestLogs.LogInformation(
                $"Let's test out rbf payments where the payment gets sent elsehwere instead");
            var invoice2 =
                user.BitPay.CreateInvoice(new Invoice() { Price = 0.01m, Currency = "BTC" }, Facade.Merchant);

            var invoice2Address =
                BitcoinAddress.Create(invoice2.BitcoinAddress, user.SupportedNetwork.NBitcoinNetwork);
            uint256 invoice2tx1Id =
                await tester.ExplorerNode.SendToAddressAsync(invoice2Address, invoice2.BtcDue, new NBitcoin.RPC.SendToAddressParameters()
                {
                    Replaceable = true
                });
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
            output.ScriptPubKey = new Key().GetScriptPubKey(ScriptPubKeyType.Legacy);
            invoice2Tx2 = await tester.ExplorerNode.SignRawTransactionAsync(invoice2Tx2);
            await tester.ExplorerNode.SendRawTransactionAsync(invoice2Tx2);
            tester.ExplorerNode.Generate(1);
            await TestUtils.EventuallyAsync(async () =>
            {
                var i = await tester.PayTester.InvoiceRepository.GetInvoice(invoice2.Id);
                Assert.Equal(InvoiceStatusLegacy.New, i.Status);
                Assert.Single(i.GetPayments(false));
                Assert.False(i.GetPayments(false).First().Accounted);
            });

            TestLogs.LogInformation("Let's test if we can RBF a normal payment without adding fees to the invoice");
            await user.SetNetworkFeeMode(NetworkFeeMode.MultiplePaymentsOnly);
            invoice = user.BitPay.CreateInvoice(new Invoice { Price = 5000.0m, Currency = "USD" }, Facade.Merchant);
            payment1 = invoice.BtcDue;
            tx1 = new uint256(tester.ExplorerNode.SendCommand("sendtoaddress", new object[]
            {
                    invoice.BitcoinAddress, payment1.ToString(), null, //comment
                    null, //comment_to
                    false, //subtractfeefromamount
                    true, //replaceable
            }).ResultString);
            TestLogs.LogInformation($"Paid {tx1}");
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
            TestLogs.LogInformation($"Bumped with {tx1Bump}");
            await TestUtils.EventuallyAsync(async () =>
                {
                    var invoiceEntity = await tester.PayTester.InvoiceRepository.GetInvoice(invoice.Id);
                    var btcPayments = invoiceEntity.GetAllBitcoinPaymentData(false).ToArray();
                    var payments = invoiceEntity.GetPayments(false).ToArray();
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

        [Fact(Timeout = TestUtils.TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanSaveKeyPathForOnChainPayments()
        {
            using var tester = CreateServerTester();
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
                    Assert.IsType<ViewResult>(await user.GetController<UIInvoiceController>().Invoice(invoice.Id)).Model)
                .Payments;
            Assert.Single(payments);
            var paymentData = payments.First().GetCryptoPaymentData() as BitcoinLikePaymentData;
            Assert.NotNull(paymentData.KeyPath);
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async void CheckCORSSetOnBitpayAPI()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            foreach (var req in new[] { "invoices/", "invoices", "rates", "tokens" }.Select(async path =>
              {
                  using HttpClient client = new HttpClient();
                  HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Options,
                          tester.PayTester.ServerUri.AbsoluteUri + path);
                  message.Headers.Add("Access-Control-Request-Headers", "test");
                  var response = await client.SendAsync(message);
                  response.EnsureSuccessStatusCode();
                  Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var val));
                  Assert.Equal("*", val.FirstOrDefault());
                  Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Headers", out val));
                  Assert.Equal("test", val.FirstOrDefault());
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

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task TestAccessBitpayAPI()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            Assert.False(user.BitPay.TestAccess(Facade.Merchant));
            user.GrantAccess();
            user.RegisterDerivationScheme("BTC");

            Assert.True(user.BitPay.TestAccess(Facade.Merchant));

            // Test request pairing code client side
            var storeController = user.GetController<UIStoresController>();
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
            Assert.IsType<RedirectToActionResult>(user.GetController<UIStoresController>()
                .GenerateAPIKey(user.StoreId).GetAwaiter().GetResult());

            var apiKey = Assert.Single(repo.GetLegacyAPIKeys(user.StoreId).GetAwaiter().GetResult());
            ///////

            // Generating a new one remove the previous
            Assert.IsType<RedirectToActionResult>(user.GetController<UIStoresController>()
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

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanUseAnyoneCanCreateInvoice()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();
            user.RegisterDerivationScheme("BTC");

            TestLogs.LogInformation("StoreId without anyone can create invoice = 403");
            var response = await tester.PayTester.HttpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, $"invoices?storeId={user.StoreId}")
                {
                    Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8,
                        "application/json"),
                });
            Assert.Equal(403, (int)response.StatusCode);

            TestLogs.LogInformation(
                "No store without  anyone can create invoice = 404 because the bitpay API can't know the storeid");
            response = await tester.PayTester.HttpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, $"invoices")
                {
                    Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8,
                        "application/json"),
                });
            Assert.Equal(404, (int)response.StatusCode);

            await user.ModifyPayment(p => p.AnyoneCanCreateInvoice = true);

            TestLogs.LogInformation("Bad store with anyone can create invoice = 403");
            response = await tester.PayTester.HttpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, $"invoices?storeId=badid")
                {
                    Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8,
                        "application/json"),
                });
            Assert.Equal(403, (int)response.StatusCode);

            TestLogs.LogInformation("Good store with anyone can create invoice = 200");
            response = await tester.PayTester.HttpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, $"invoices?storeId={user.StoreId}")
                {
                    Content = new StringContent("{\"Price\": 5000, \"currency\": \"USD\"}", Encoding.UTF8,
                        "application/json"),
                });
            Assert.Equal(200, (int)response.StatusCode);
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanTweakRate()
        {
            using var tester = CreateServerTester();
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

            var storeController = user.GetController<UIStoresController>();
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


        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCreateTopupInvoices()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();
            user.RegisterDerivationScheme("BTC");

            var rng = new Random();
            var seed = rng.Next();
            rng = new Random(seed);
            TestLogs.LogInformation("Seed: " + seed);
            foreach (var networkFeeMode in Enum.GetValues(typeof(NetworkFeeMode)).Cast<NetworkFeeMode>())
            {
                await user.SetNetworkFeeMode(networkFeeMode);
                await AssertTopUpBtcPrice(tester, user, Money.Coins(1.0m), 5000.0m, networkFeeMode);
                await AssertTopUpBtcPrice(tester, user, Money.Coins(1.23456789m), 5000.0m * 1.23456789m, networkFeeMode);
                // Check if there is no strange roundup issues
                var v = (decimal)(rng.NextDouble() + 1.0);
                v = Money.Coins(v).ToDecimal(MoneyUnit.BTC);
                await AssertTopUpBtcPrice(tester, user, Money.Coins(v), 5000.0m * v, networkFeeMode);
            }
        }

        private static async Task AssertTopUpBtcPrice(ServerTester tester, TestAccount user, Money btcSent, decimal expectedPriceWithoutNetworkFee, NetworkFeeMode networkFeeMode)
        {
            var cashCow = tester.ExplorerNode;
            // First we try payment with a merchant having only BTC
            var client = await user.CreateClient();
            var invoice = await client.CreateInvoice(user.StoreId, new CreateInvoiceRequest()
            {
                Amount = null,
                Currency = "USD"
            });
            Assert.Equal(0m, invoice.Amount);
            Assert.Equal(InvoiceType.TopUp, invoice.Type);
            var btcmethod = (await client.GetInvoicePaymentMethods(user.StoreId, invoice.Id))[0];
            var paid = btcSent;
            var invoiceAddress = BitcoinAddress.Create(btcmethod.Destination, cashCow.Network);


            var btc = new PaymentMethodId("BTC", PaymentTypes.BTCLike);
            var networkFee = (await tester.PayTester.InvoiceRepository.GetInvoice(invoice.Id))
                            .GetPaymentMethods()[btc]
                            .GetPaymentMethodDetails()
                            .AssertType<BitcoinLikeOnChainPaymentMethod>()
                            .GetNextNetworkFee();
            if (networkFeeMode != NetworkFeeMode.Always)
            {
                networkFee = 0.0m;
            }

            cashCow.SendToAddress(invoiceAddress, paid);


            await TestUtils.EventuallyAsync(async () =>
            {
                try
                {
                    var bitpayinvoice = await user.BitPay.GetInvoiceAsync(invoice.Id);
                    Assert.NotEqual(0.0m, bitpayinvoice.Price);
                    var due = Money.Parse(bitpayinvoice.CryptoInfo[0].CryptoPaid);
                    Assert.Equal(paid, due);
                    Assert.Equal(expectedPriceWithoutNetworkFee - networkFee * bitpayinvoice.Rate, bitpayinvoice.Price);
                    Assert.Equal(Money.Zero, bitpayinvoice.BtcDue);
                    Assert.Equal("paid", bitpayinvoice.Status);
                    Assert.Equal("False", bitpayinvoice.ExceptionStatus.ToString());

                    // Check if we index by price correctly once we know it
                    var invoices = await client.GetInvoices(user.StoreId, textSearch: $"{bitpayinvoice.Price.ToString(CultureInfo.InvariantCulture)}");
                    Assert.Contains(invoices, inv => inv.Id == bitpayinvoice.Id);
                }
                catch (JsonSerializationException)
                {
                    Assert.False(true, "The bitpay's amount is not set");
                }
            });
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanModifyRates()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();
            user.RegisterDerivationScheme("BTC");

            var store = user.GetController<UIStoresController>();
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
            store = user.GetController<UIStoresController>();
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
            store = user.GetController<UIStoresController>();
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
            store = user.GetController<UIStoresController>();
            rateVm = Assert.IsType<RatesViewModel>(Assert.IsType<ViewResult>(store.Rates()).Model);
            Assert.Equal(50, rateVm.Spread);
            Assert.True(rateVm.ShowScripting);
            Assert.Contains("DOGE_X", rateVm.Script, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUseDefaultCurrency()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync(true);
            user.RegisterDerivationScheme("BTC");
            await user.ModifyPayment(s =>
            {
                Assert.Equal("USD", s.DefaultCurrency);
                s.DefaultCurrency = "EUR";
            });
            var client = await user.CreateClient();

            // with greenfield
            var invoice = await client.CreateInvoice(user.StoreId, new CreateInvoiceRequest());
            Assert.Equal("EUR", invoice.Currency);
            Assert.Equal(InvoiceType.TopUp, invoice.Type);

            // with bitpay api
            var invoice2 = await user.BitPay.CreateInvoiceAsync(new Invoice());
            Assert.Equal("EUR", invoice2.Currency);

            // via UI
            var controller = user.GetController<UIInvoiceController>();
            var model = await controller.CreateInvoice();
            (await controller.CreateInvoice(new CreateInvoiceModel(), default)).AssertType<RedirectToActionResult>();
            invoice = await client.GetInvoice(user.StoreId, controller.CreatedInvoiceId);
            Assert.Equal("EUR", invoice.Currency);
            Assert.Equal(InvoiceType.TopUp, invoice.Type);

            // Check that the SendWallet use the default currency
            var walletController = user.GetController<UIWalletsController>();
            var walletSend = await walletController.WalletSend(new WalletId(user.StoreId, "BTC")).AssertViewModelAsync<WalletSendModel>();
            Assert.Equal("EUR", walletSend.Fiat);
        }

        [Fact]
        [Trait("Lightning", "Lightning")]
        public async Task CanSetPaymentMethodLimits()
        {
            using var tester = CreateServerTester();
            tester.ActivateLightning();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess(true);
            user.RegisterDerivationScheme("BTC");
            await user.RegisterLightningNodeAsync("BTC");


            var lnMethod = new PaymentMethodId("BTC", PaymentTypes.LightningLike).ToString();
            var btcMethod = new PaymentMethodId("BTC", PaymentTypes.BTCLike).ToString();

            // We allow BTC and LN, but not BTC under 5 USD, so only LN should be in the invoice
            var vm = Assert.IsType<CheckoutAppearanceViewModel>(Assert
                .IsType<ViewResult>(user.GetController<UIStoresController>().CheckoutAppearance()).Model);
            Assert.Equal(2, vm.PaymentMethodCriteria.Count);
            var criteria = Assert.Single(vm.PaymentMethodCriteria.Where(m => m.PaymentMethod == btcMethod.ToString()));
            Assert.Equal(new PaymentMethodId("BTC", BitcoinPaymentType.Instance).ToString(), criteria.PaymentMethod);
            criteria.Value = "5 USD";
            criteria.Type = PaymentMethodCriteriaViewModel.CriteriaType.GreaterThan;
            Assert.IsType<RedirectToActionResult>(user.GetController<UIStoresController>().CheckoutAppearance(vm)
                .Result);

            var invoice = user.BitPay.CreateInvoice(
                new Invoice
                {
                    Price = 4.5m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);

            Assert.Single(invoice.CryptoInfo);
            Assert.Equal(PaymentTypes.LightningLike.ToString(), invoice.CryptoInfo[0].PaymentType);

            // Let's replicate https://github.com/btcpayserver/btcpayserver/issues/2963
            // We allow BTC for more than 5 USD, and LN for less than 150. The default is LN, so the default
            // payment method should be LN.
            vm = Assert.IsType<CheckoutAppearanceViewModel>(Assert
                .IsType<ViewResult>(user.GetController<UIStoresController>().CheckoutAppearance()).Model);
            vm.DefaultPaymentMethod = lnMethod;
            criteria = vm.PaymentMethodCriteria.First();
            criteria.Value = "150 USD";
            criteria.Type = PaymentMethodCriteriaViewModel.CriteriaType.LessThan;
            criteria = vm.PaymentMethodCriteria.Skip(1).First();
            criteria.Value = "5 USD";
            criteria.Type = PaymentMethodCriteriaViewModel.CriteriaType.GreaterThan;
            Assert.IsType<RedirectToActionResult>(user.GetController<UIStoresController>().CheckoutAppearance(vm)
                .Result);
            invoice = user.BitPay.CreateInvoice(
               new Invoice()
               {
                   Price = 50m,
                   Currency = "USD",
                   PosData = "posData",
                   OrderId = "orderId",
                   ItemDesc = "Some description",
                   FullNotifications = true
               }, Facade.Merchant);
            var checkout = (await user.GetController<UIInvoiceController>().Checkout(invoice.Id)).AssertViewModel<PaymentModel>();
            Assert.Equal(lnMethod, checkout.PaymentMethodId);

            // If we change store's default, it should change the checkout's default
            vm.DefaultPaymentMethod = btcMethod;
            Assert.IsType<RedirectToActionResult>(user.GetController<UIStoresController>().CheckoutAppearance(vm)
                .Result);
            checkout = (await user.GetController<UIInvoiceController>().Checkout(invoice.Id)).AssertViewModel<PaymentModel>();
            Assert.Equal(btcMethod, checkout.PaymentMethodId);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanSetUnifiedQrCode()
        {
            using var tester = CreateServerTester();
            tester.ActivateLightning();
            await tester.StartAsync();
            await tester.EnsureChannelsSetup();
            var user = tester.NewAccount();
            var cryptoCode = "BTC";
            await user.GrantAccessAsync(true);
            user.RegisterDerivationScheme(cryptoCode, ScriptPubKeyType.Segwit);
            user.RegisterLightningNode(cryptoCode, LightningConnectionType.CLightning);

            var invoice = user.BitPay.CreateInvoice(
                new Invoice
                {
                    Price = 5.5m,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some description",
                    FullNotifications = true
                }, Facade.Merchant);

            // validate that invoice data model doesn't have lightning string initially
            var res = await user.GetController<UIInvoiceController>().Checkout(invoice.Id);
            var paymentMethodFirst = Assert.IsType<PaymentModel>(
                Assert.IsType<ViewResult>(res).Model
            );
            Assert.DoesNotContain("&lightning=", paymentMethodFirst.InvoiceBitcoinUrlQR);

            // enable unified QR code in settings
            var vm = Assert.IsType<LightningSettingsViewModel>(Assert
                .IsType<ViewResult>(user.GetController<UIStoresController>().LightningSettings(user.StoreId, cryptoCode)).Model
            );
            vm.OnChainWithLnInvoiceFallback = true;
            Assert.IsType<RedirectToActionResult>(
                user.GetController<UIStoresController>().LightningSettings(vm).Result
            );

            // validate that QR code now has both onchain and offchain payment urls
            res = await user.GetController<UIInvoiceController>().Checkout(invoice.Id);
            var paymentMethodSecond = Assert.IsType<PaymentModel>(
                Assert.IsType<ViewResult>(res).Model
            );
            Assert.Contains("&lightning=", paymentMethodSecond.InvoiceBitcoinUrlQR);
            Assert.StartsWith("bitcoin:", paymentMethodSecond.InvoiceBitcoinUrlQR);
            var split = paymentMethodSecond.InvoiceBitcoinUrlQR.Split('?')[0];

            // Standard for all uppercase characters in QR codes is still not implemented in all wallets
            // But we're proceeding with BECH32 being uppercase
            Assert.True($"bitcoin:{paymentMethodSecond.BtcAddress.ToUpperInvariant()}" == split);

            // Fallback lightning invoice should be uppercase inside the QR code.
            var lightningFallback = paymentMethodSecond.InvoiceBitcoinUrlQR.Split(new[] { "&lightning=" }, StringSplitOptions.None)[1];
            Assert.True(lightningFallback.ToUpperInvariant() == lightningFallback);
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanSetPaymentMethodLimitsLightning()
        {
            using var tester = CreateServerTester();
            tester.ActivateLightning();
            await tester.StartAsync();
            await tester.EnsureChannelsSetup();
            var user = tester.NewAccount();
            var cryptoCode = "BTC";
            user.GrantAccess(true);
            user.RegisterLightningNode(cryptoCode, LightningConnectionType.Charge);
            var vm = user.GetController<UIStoresController>().CheckoutAppearance().AssertViewModel<CheckoutAppearanceViewModel>();
            var criteria = Assert.Single(vm.PaymentMethodCriteria);
            Assert.Equal(new PaymentMethodId(cryptoCode, LightningPaymentType.Instance).ToString(), criteria.PaymentMethod);
            criteria.Value = "2 USD";
            criteria.Type = PaymentMethodCriteriaViewModel.CriteriaType.LessThan;
            Assert.IsType<RedirectToActionResult>(user.GetController<UIStoresController>().CheckoutAppearance(vm)
                .Result);

            var invoice = user.BitPay.CreateInvoice(
                new Invoice
                {
                    Price = 1.5m,
                    Currency = "USD"
                }, Facade.Merchant);

            Assert.Single(invoice.CryptoInfo);
            Assert.Equal(PaymentTypes.LightningLike.ToString(), invoice.CryptoInfo[0].PaymentType);

            // Activating LNUrl, we should still have only 1 payment criteria that can be set.
            user.RegisterLightningNode(cryptoCode, LightningConnectionType.Charge);
            var lnSettingsVm = user.GetController<UIStoresController>().LightningSettings(user.StoreId, cryptoCode).AssertViewModel<LightningSettingsViewModel>();
            lnSettingsVm.LNURLEnabled = true;
            lnSettingsVm.LNURLStandardInvoiceEnabled = true;
            Assert.IsType<RedirectToActionResult>(user.GetController<UIStoresController>().LightningSettings(lnSettingsVm).Result);
            vm = user.GetController<UIStoresController>().CheckoutAppearance().AssertViewModel<CheckoutAppearanceViewModel>();
            criteria = Assert.Single(vm.PaymentMethodCriteria);
            Assert.Equal(new PaymentMethodId(cryptoCode, LightningPaymentType.Instance).ToString(), criteria.PaymentMethod);
            Assert.IsType<RedirectToActionResult>(user.GetController<UIStoresController>().CheckoutAppearance(vm).Result);

            // However, creating an invoice should show LNURL
            invoice = user.BitPay.CreateInvoice(
                new Invoice
                {
                    Price = 1.5m,
                    Currency = "USD"
                }, Facade.Merchant);
            Assert.Equal(2, invoice.CryptoInfo.Length);

            // Make sure this throw: Since BOLT11 and LN Url share the same criteria, there should be no payment method available
            Assert.Throws<BitPayException>(() => user.BitPay.CreateInvoice(
                new Invoice
                {
                    Price = 2.5m,
                    Currency = "USD"
                }, Facade.Merchant));
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task PosDataParser_ParsesCorrectly_Slower()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();
            user.RegisterDerivationScheme("BTC");

            var controller = tester.PayTester.GetController<UIInvoiceController>(null);

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

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanExportInvoicesJson()
        {
            decimal GetFieldValue(string input, string fieldName)
            {
                var match = Regex.Match(input, $"\"{fieldName}\":([^,]*)");
                Assert.True(match.Success);
                return decimal.Parse(match.Groups[1].Value.Trim(), CultureInfo.InvariantCulture);
            }

            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            await user.SetNetworkFeeMode(NetworkFeeMode.Always);
            var invoice = await user.BitPay.CreateInvoiceAsync(
                new Invoice
                {
                    Price = 10,
                    Currency = "USD",
                    PosData = "posData",
                    OrderId = "orderId",
                    ItemDesc = "Some \", description",
                    FullNotifications = true
                }, Facade.Merchant);

            var networkFee = new FeeRate(invoice.MinerFees["BTC"].SatoshiPerBytes).GetFee(100);
            var jsonResult = user.GetController<UIInvoiceController>().Export("json").GetAwaiter().GetResult();
            var result = Assert.IsType<ContentResult>(jsonResult);
            Assert.Equal("application/json", result.ContentType);
            Assert.Single(JArray.Parse(result.Content));

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
                    user.GetController<UIInvoiceController>().Export("json").GetAwaiter().GetResult();
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

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanChangeNetworkFeeMode()
        {
            using var tester = CreateServerTester();
            var btc = new PaymentMethodId("BTC", PaymentTypes.BTCLike);
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();
            user.RegisterDerivationScheme("BTC");
            foreach (var networkFeeMode in Enum.GetValues(typeof(NetworkFeeMode)).Cast<NetworkFeeMode>())
            {
                TestLogs.LogInformation($"Trying with {nameof(networkFeeMode)}={networkFeeMode}");
                await user.SetNetworkFeeMode(networkFeeMode);
                var invoice = user.BitPay.CreateInvoice(
                    new Invoice
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
                TestLogs.LogInformation(
                    $"Product part due is {productPartDue} and due {due} with network fee {nextNetworkFee}");
                Assert.Equal(productPartDue + nextNetworkFee, due.ToDecimal(MoneyUnit.BTC));
                var firstPayment = productPartDue - missingMoney;
                cashCow.SendToAddress(invoiceAddress, Money.Coins(firstPayment));

                await TestUtils.EventuallyAsync(async () =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    due = Money.Parse(invoice.CryptoInfo[0].Due);
                    TestLogs.LogInformation($"Remaining due after first payment: {due}");
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
                TestLogs.LogInformation($"After payment of {due}, the invoice should be paid");
                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal("paid", invoice.Status);
                });
            }
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanExportInvoicesCsv()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            await user.SetNetworkFeeMode(NetworkFeeMode.Always);
            var invoice = user.BitPay.CreateInvoice(
                new Invoice
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
                    user.GetController<UIInvoiceController>().Export("csv").GetAwaiter().GetResult();
                var paidresult = Assert.IsType<ContentResult>(exportResultPaid);
                Assert.Equal("application/csv", paidresult.ContentType);
                Assert.Contains($",orderId,{invoice.Id},", paidresult.Content);
                Assert.Contains($",On-Chain,BTC,0.0991,0.0001,5000.0", paidresult.Content);
                Assert.Contains($",USD,5.00", paidresult.Content); // Seems hacky but some plateform does not render this decimal the same
                    Assert.Contains("0,,\"Some \"\", description\",New (paidPartial),new,paidPartial",
                    paidresult.Content);
            });
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCreateAndDeleteApps()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            var user2 = tester.NewAccount();
            await user2.GrantAccessAsync();
            var stores = user.GetController<UIStoresController>();
            var apps = user.GetController<UIAppsController>();
            var apps2 = user2.GetController<UIAppsController>();
            var pos = user.GetController<UIPointOfSaleController>();
            var vm = Assert.IsType<CreateAppViewModel>(Assert.IsType<ViewResult>(apps.CreateApp(user.StoreId)).Model);
            var appType = AppType.PointOfSale.ToString();
            Assert.NotNull(vm.SelectedAppType);
            Assert.Null(vm.AppName);
            vm.AppName = "test";
            vm.SelectedAppType = appType;
            var redirectToAction = Assert.IsType<RedirectToActionResult>(apps.CreateApp(user.StoreId, vm).Result);
            Assert.Equal(nameof(pos.UpdatePointOfSale), redirectToAction.ActionName);
            var appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            var appList2 =
                Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps2.ListApps(user2.StoreId).Result).Model);
            var app = appList.Apps[0];
            var appData = new AppData { Id = app.Id, StoreDataId = app.StoreId, Name = app.AppName, AppType = appType };
            apps.HttpContext.SetAppData(appData);
            pos.HttpContext.SetAppData(appData);
            Assert.Single(appList.Apps);
            Assert.Empty(appList2.Apps);
            Assert.Equal("test", appList.Apps[0].AppName);
            Assert.Equal(apps.CreatedAppId, appList.Apps[0].Id);
            Assert.True(app.IsOwner);
            Assert.Equal(user.StoreId, appList.Apps[0].StoreId);
            Assert.IsType<NotFoundResult>(apps2.DeleteApp(appList.Apps[0].Id));
            Assert.IsType<ViewResult>(apps.DeleteApp(appList.Apps[0].Id));
            redirectToAction = Assert.IsType<RedirectToActionResult>(apps.DeleteAppPost(appList.Apps[0].Id).Result);
            Assert.Equal(nameof(stores.Dashboard), redirectToAction.ActionName);
            appList = Assert.IsType<ListAppsViewModel>(Assert.IsType<ViewResult>(apps.ListApps(user.StoreId).Result).Model);
            Assert.Empty(appList.Apps);
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        [Trait("Lightning", "Lightning")]
        public async Task CanCreateStrangeInvoice()
        {
            using var tester = CreateServerTester();
            tester.ActivateLightning();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess(true);
            user.RegisterDerivationScheme("BTC");

            DateTimeOffset expiration = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(21);

            // This should fail, the amount is too low to be above the dust limit of bitcoin
            var ex = Assert.Throws<BitPayException>(() => user.BitPay.CreateInvoice(
                new Invoice()
                {
                    Price = 0.000000012m,
                    Currency = "USD",
                    FullNotifications = true,
                    ExpirationTime = expiration
                }, Facade.Merchant));
            Assert.Contains("dust threshold", ex.Message);
            await user.RegisterLightningNodeAsync("BTC");

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

            // Should round down to 50.51, taxIncluded should be also clipped to this value because taxIncluded can't be higher than the price.
            var invoice5 = user.BitPay.CreateInvoice(
                new Invoice() { Price = 50.513m, Currency = "USD", FullNotifications = true, TaxIncluded = 50.516m }, Facade.Merchant);
            Assert.Equal(50.51m, invoice5.Price);
            Assert.Equal(50.51m, invoice5.TaxIncluded);

            var greenfield = await user.CreateClient();
            var invoice5g = await greenfield.CreateInvoice(user.StoreId, new CreateInvoiceRequest()
            {
                Amount = 50.513m,
                Currency = "USD",
                Metadata = new JObject() { new JProperty("taxIncluded", 50.516m), new JProperty("orderId", "000000161") }
            });
            Assert.Equal(50.51m, invoice5g.Amount);
            Assert.Equal(50.51m, (decimal)invoice5g.Metadata["taxIncluded"]);
            Assert.Equal("000000161", (string)invoice5g.Metadata["orderId"]);

            var zeroInvoice = await greenfield.CreateInvoice(user.StoreId, new CreateInvoiceRequest()
            {
                Amount = 0m,
                Currency = "USD"
            });
            Assert.Equal(InvoiceStatus.New, zeroInvoice.Status);
            await TestUtils.EventuallyAsync(async () =>
            {
                zeroInvoice = await greenfield.GetInvoice(user.StoreId, zeroInvoice.Id);
                Assert.Equal(InvoiceStatus.Settled, zeroInvoice.Status);
            });

            var zeroInvoicePM = await greenfield.GetInvoicePaymentMethods(user.StoreId, zeroInvoice.Id);
            Assert.Empty(zeroInvoicePM);
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task InvoiceFlowThroughDifferentStatesCorrectly()
        {
            using var tester = CreateServerTester();
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
                    Assert.Equal(new PaymentMethodId("BTC", PaymentTypes.BTCLike).ToStringNormalized(), c.PaymentMethod);
                    Assert.NotNull(c.Payment);
                    Assert.Equal(invoice.BitcoinAddress, c.Payment.Destination);
                    Assert.StartsWith(txId.ToString(), c.Payment.Id);

                });
            user.AssertHasWebhookEvent<WebhookInvoicePaymentSettledEvent>(WebhookEventType.InvoicePaymentSettled,
                c =>
                {
                    Assert.False(c.AfterExpiration);
                    Assert.Equal(new PaymentMethodId("BTC", PaymentTypes.BTCLike).ToStringNormalized(), c.PaymentMethod);
                    Assert.NotNull(c.Payment);
                    Assert.Equal(invoice.BitcoinAddress, c.Payment.Destination);
                    Assert.StartsWith(txId.ToString(), c.Payment.Id);
                });
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CheckLogsRoute()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();
            user.RegisterDerivationScheme("BTC");

            var serverController = user.GetController<UIServerController>();
            var vm = Assert.IsType<LogsViewModel>(
                Assert.IsType<ViewResult>(await serverController.LogsView()).Model);
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanLoginWithNoSecondaryAuthSystemsOrRequestItWhenAdded()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();

            var accountController = tester.PayTester.GetController<UIAccountController>();

            //no 2fa or fido2 enabled, login should work
            Assert.Equal(nameof(UIHomeController.Index),
                Assert.IsType<RedirectToActionResult>(await accountController.Login(new LoginViewModel()
                {
                    Email = user.RegisterDetails.Email,
                    Password = user.RegisterDetails.Password
                })).ActionName);

            var listController = user.GetController<UIManageController>();
            var manageController = user.GetController<UIFido2Controller>();

            //by default no fido2 devices available
            Assert.Empty(Assert
                .IsType<TwoFactorAuthenticationViewModel>(Assert
                    .IsType<ViewResult>(await listController.TwoFactorAuthentication()).Model).Credentials);
            Assert.IsType<CredentialCreateOptions>(Assert
                    .IsType<ViewResult>(await manageController.Create(new AddFido2CredentialViewModel
                    {
                        Name = "label"
                    })).Model);

            //sending an invalid response model back to server, should error out
            Assert.IsType<RedirectToActionResult>(await manageController.CreateResponse("sdsdsa", "sds"));
            var statusModel = manageController.TempData.GetStatusMessageModel();
            Assert.Equal(StatusMessageModel.StatusSeverity.Error, statusModel.Severity);

            var contextFactory = tester.PayTester.GetService<ApplicationDbContextFactory>();

            //add a fake fido2 device in db directly since emulating a fido2 device is hard and annoying
            using (var context = contextFactory.CreateContext())
            {
                var newDevice = new Fido2Credential()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "fake",
                    Type = Fido2Credential.CredentialType.FIDO2,
                    ApplicationUserId = user.UserId
                };
                newDevice.SetBlob(new Fido2CredentialBlob() { });
                await context.Fido2Credentials.AddAsync(newDevice);
                await context.SaveChangesAsync();

                Assert.NotNull(newDevice.Id);
                Assert.NotEmpty(Assert
                    .IsType<TwoFactorAuthenticationViewModel>(Assert
                        .IsType<ViewResult>(await listController.TwoFactorAuthentication()).Model).Credentials);
            }

            //check if we are showing the fido2 login screen now
            var secondLoginResult = Assert.IsType<ViewResult>(await accountController.Login(new LoginViewModel()
            {
                Email = user.RegisterDetails.Email,
                Password = user.RegisterDetails.Password
            }));

            Assert.Equal("SecondaryLogin", secondLoginResult.ViewName);
            var vm = Assert.IsType<SecondaryLoginViewModel>(secondLoginResult.Model);
            //2fa was never enabled for user so this should be empty
            Assert.Null(vm.LoginWith2FaViewModel);
            Assert.NotNull(vm.LoginWithFido2ViewModel);
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async void CheckOnionlocationForNonOnionHtmlRequests()
        {
            using var tester = CreateServerTester();
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

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanCheckForNewVersion()
        {
            using var tester = CreateServerTester(newDb: true);
            await tester.StartAsync();

            var acc = tester.NewAccount();
            acc.GrantAccess(true);

            var settings = tester.PayTester.GetService<SettingsRepository>();
            await settings.UpdateSetting<PoliciesSettings>(new PoliciesSettings() { CheckForNewVersions = true });

            var mockEnv = tester.PayTester.GetService<BTCPayServerEnvironment>();
            var mockSender = tester.PayTester.GetService<Services.Notifications.NotificationSender>();

            var svc = new NewVersionCheckerHostedService(settings, mockEnv, mockSender, new MockVersionFetcher(), BTCPayLogs);
            await svc.ProcessVersionCheck();

            // since last version present in database was null, it should've been updated with version mock returned
            var lastVersion = await settings.GetSettingAsync<NewVersionCheckerDataHolder>();
            Assert.Equal(MockVersionFetcher.MOCK_NEW_VERSION, lastVersion.LastVersion);

            // we should also have notification in UI
            var ctrl = acc.GetController<UINotificationsController>();
            var newVersion = MockVersionFetcher.MOCK_NEW_VERSION;

            var vm = Assert.IsType<Models.NotificationViewModels.IndexViewModel>(
                Assert.IsType<ViewResult>(await ctrl.Index()).Model);

            Assert.True(vm.Skip == 0);
            Assert.True(vm.Count == 50);
            Assert.Null(vm.Total);
            Assert.True(vm.Items.Count == 1);

            var fn = vm.Items.First();
            var now = DateTimeOffset.UtcNow;
            Assert.True(fn.Created >= now.AddSeconds(-3));
            Assert.True(fn.Created <= now);
            Assert.Equal($"New version {newVersion} released!", fn.Body);
            Assert.Equal($"https://github.com/btcpayserver/btcpayserver/releases/tag/v{newVersion}", fn.ActionLink);
            Assert.False(fn.Seen);
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanDoLightningInternalNodeMigration()
        {
            using var tester = CreateServerTester(newDb: true);
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

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        [Obsolete]
        public async Task CanDoLabelMigrations()
        {
            using var tester = CreateServerTester(newDb: true);
            await tester.StartAsync();
            var dbf = tester.PayTester.GetService<ApplicationDbContextFactory>();
            int walletCount = 1000;
            var wallet = "walletttttttttttttttttttttttttttt";
            using (var db = dbf.CreateContext())
            {
                for (int i = 0; i < walletCount; i++)
                {
                    var walletData = new WalletData() { Id = $"S-{wallet}{i}-BTC" };
                    walletData.Blob = ZipUtils.Zip("{\"LabelColors\": { \"label1\" : \"black\", \"payout\":\"green\" }}");
                    db.Wallets.Add(walletData);
                }
                await db.SaveChangesAsync();
            }
            uint256 firstTxId = null;
            using (var db = dbf.CreateContext())
            {
                int transactionCount = 10_000;
                for (int i = 0; i < transactionCount; i++)
                {
                    var txId = RandomUtils.GetUInt256();
                    var wt = new WalletTransactionData()
                    {
                        WalletDataId = $"S-{wallet}{i % walletCount}-BTC",
                        TransactionId = txId.ToString(),
                    };
                    firstTxId ??= txId;
                    if (i != 10)
                        wt.Blob = ZipUtils.Zip("{\"Comment\":\"test\"}");
                    if (i % 1240 != 0)
                    {
                        wt.Labels = "[{\"type\":\"raw\", \"text\":\"label1\"}]";
                    }
                    else if (i == 0)
                    {
                        wt.Labels = "[{\"type\":\"raw\", \"text\":\"label1\"},{\"type\":\"raw\", \"text\":\"labelo" + i + "\"}, " +
                            "{\"type\":\"payout\", \"text\":\"payout\", \"pullPaymentPayouts\":{\"pp1\":[\"p1\",\"p2\"],\"pp2\":[\"p3\"]}}]";
                    }
                    else
                    {
                        wt.Labels = "[{\"type\":\"raw\", \"text\":\"label1\"},{\"type\":\"raw\", \"text\":\"labelo" + i + "\"}]";
                    }
                    db.WalletTransactions.Add(wt);
                }
                await db.SaveChangesAsync();
            }
            await RestartMigration(tester);
            var migrator = tester.PayTester.GetService<IEnumerable<IHostedService>>().OfType<DbMigrationsHostedService>().First();
            await migrator.MigratedTransactionLabels(0);

            var walletRepo = tester.PayTester.GetService<WalletRepository>();
            var wi1 = await walletRepo.GetWalletLabels(new WalletId($"{wallet}0", "BTC"));
            Assert.Equal(3, wi1.Length);
            Assert.Contains(wi1, o => o.Label == "label1" && o.Color == "black");
            Assert.Contains(wi1, o => o.Label == "labelo0" && o.Color == "#000");
            Assert.Contains(wi1, o => o.Label == "payout" && o.Color == "green");

            var txInfo = await walletRepo.GetWalletTransactionsInfo(new WalletId($"{wallet}0", "BTC"), new[] { firstTxId.ToString() });
            Assert.Equal("test", txInfo.Values.First().Comment);
            // Should have the 2 raw labels, and one legacy label for payouts
            Assert.Equal(3, txInfo.Values.First().LegacyLabels.Count);
            var payoutLabel = txInfo.Values.First().LegacyLabels.Select(l => l.Value).OfType<PayoutLabel>().First();
            Assert.Equal(2, payoutLabel.PullPaymentPayouts.Count);
            Assert.Equal(2, payoutLabel.PullPaymentPayouts["pp1"].Count);
            Assert.Single(payoutLabel.PullPaymentPayouts["pp2"]);
        }


        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanDoInvoiceMigrations()
        {
            using var tester = CreateServerTester(newDb: true);
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

        private static async Task RestartMigration(ServerTester tester)
        {
            var settings = tester.PayTester.GetService<SettingsRepository>();
            await settings.UpdateSetting<MigrationSettings>(new MigrationSettings());
            var migrationStartupTask = tester.PayTester.GetService<IServiceProvider>().GetServices<IStartupTask>()
                .Single(task => task is MigrationStartupTask);
            await migrationStartupTask.ExecuteAsync();
        }

        [Fact(Timeout = LongRunningTestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task EmailSenderTests()
        {
            using var tester = CreateServerTester(newDb: true);
            await tester.StartAsync();

            var acc = tester.NewAccount();
            acc.GrantAccess(true);

            var settings = tester.PayTester.GetService<SettingsRepository>();
            var emailSenderFactory = tester.PayTester.GetService<EmailSenderFactory>();

            Assert.Null(await Assert.IsType<ServerEmailSender>(await emailSenderFactory.GetEmailSender()).GetEmailSettings());
            Assert.Null(await Assert.IsType<StoreEmailSender>(await emailSenderFactory.GetEmailSender(acc.StoreId)).GetEmailSettings());


            await settings.UpdateSetting(new PoliciesSettings() { DisableStoresToUseServerEmailSettings = false });
            await settings.UpdateSetting(new EmailSettings()
            {
                From = "admin@admin.com",
                Login = "admin@admin.com",
                Password = "admin@admin.com",
                Port = 1234,
                Server = "admin.com",
            });
            Assert.Equal("admin@admin.com", (await Assert.IsType<ServerEmailSender>(await emailSenderFactory.GetEmailSender()).GetEmailSettings()).Login);
            Assert.Equal("admin@admin.com", (await Assert.IsType<StoreEmailSender>(await emailSenderFactory.GetEmailSender(acc.StoreId)).GetEmailSettings()).Login);

            await settings.UpdateSetting(new PoliciesSettings() { DisableStoresToUseServerEmailSettings = true });
            Assert.Equal("admin@admin.com", (await Assert.IsType<ServerEmailSender>(await emailSenderFactory.GetEmailSender()).GetEmailSettings()).Login);
            Assert.Null(await Assert.IsType<StoreEmailSender>(await emailSenderFactory.GetEmailSender(acc.StoreId)).GetEmailSettings());

            Assert.IsType<RedirectToActionResult>(await acc.GetController<UIStoresController>().StoreEmailSettings(acc.StoreId, new EmailsViewModel(new EmailSettings()
            {
                From = "store@store.com",
                Login = "store@store.com",
                Password = "store@store.com",
                Port = 1234,
                Server = "store.com"
            }), ""));

            Assert.Equal("store@store.com", (await Assert.IsType<StoreEmailSender>(await emailSenderFactory.GetEmailSender(acc.StoreId)).GetEmailSettings()).Login);
        }

        [Fact(Timeout = TestUtils.TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task CanConfigureStorage()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();
            var controller = tester.PayTester.GetController<UIServerController>(user.UserId, user.StoreId);


            //Once we select a provider, redirect to its view
            var localResult = Assert
                .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                {
                    Provider = StorageProvider.FileSystem
                }));
            Assert.Equal(nameof(UIServerController.StorageProvider), localResult.ActionName);
            Assert.Equal(StorageProvider.FileSystem.ToString(), localResult.RouteValues["provider"]);


            var AmazonS3result = Assert
                .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                {
                    Provider = StorageProvider.AmazonS3
                }));
            Assert.Equal(nameof(UIServerController.StorageProvider), AmazonS3result.ActionName);
            Assert.Equal(StorageProvider.AmazonS3.ToString(), AmazonS3result.RouteValues["provider"]);

            var GoogleResult = Assert
                .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                {
                    Provider = StorageProvider.GoogleCloudStorage
                }));
            Assert.Equal(nameof(UIServerController.StorageProvider), GoogleResult.ActionName);
            Assert.Equal(StorageProvider.GoogleCloudStorage.ToString(), GoogleResult.RouteValues["provider"]);


            var AzureResult = Assert
                .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                {
                    Provider = StorageProvider.AzureBlobStorage
                }));
            Assert.Equal(nameof(UIServerController.StorageProvider), AzureResult.ActionName);
            Assert.Equal(StorageProvider.AzureBlobStorage.ToString(), AzureResult.RouteValues["provider"]);

            //Cool, we get redirected to the config pages
            //Let's configure this stuff

            //Let's try and cheat and go to an invalid storage provider config
            Assert.Equal(nameof(Storage), (Assert
                .IsType<RedirectToActionResult>(await controller.StorageProvider("I am not a real provider"))
                .ActionName));

            //ok no more messing around, let's configure this shit. 
            var fileSystemStorageConfiguration = Assert.IsType<FileSystemStorageConfiguration>(Assert
                .IsType<ViewResult>(await controller.StorageProvider(StorageProvider.FileSystem.ToString()))
                .Model);

            //local file system does not need config, easy days!
            Assert.IsType<ViewResult>(
                await controller.EditFileSystemStorageProvider(fileSystemStorageConfiguration));

            //ok cool, let's see if this got set right
            var shouldBeRedirectingToLocalStorageConfigPage =
                Assert.IsType<RedirectToActionResult>(await controller.Storage());
            Assert.Equal(nameof(StorageProvider), shouldBeRedirectingToLocalStorageConfigPage.ActionName);
            Assert.Equal(StorageProvider.FileSystem,
                shouldBeRedirectingToLocalStorageConfigPage.RouteValues["provider"]);


            //if we tell the settings page to force, it should allow us to select a new provider
            Assert.IsType<ChooseStorageViewModel>(Assert.IsType<ViewResult>(await controller.Storage(true)).Model);

            //awesome, now let's see if the files result says we're all set up
            var viewFilesViewModel =
                Assert.IsType<ViewFilesViewModel>(Assert.IsType<ViewResult>(await controller.Files()).Model);
            Assert.True(viewFilesViewModel.StorageConfigured);
            Assert.Empty(viewFilesViewModel.Files);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanUseLocalProviderFiles()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            user.GrantAccess();
            var controller = tester.PayTester.GetController<UIServerController>(user.UserId, user.StoreId);

            var fileSystemStorageConfiguration = Assert.IsType<FileSystemStorageConfiguration>(Assert
                .IsType<ViewResult>(await controller.StorageProvider(StorageProvider.FileSystem.ToString()))
                .Model);
            Assert.IsType<ViewResult>(
                await controller.EditFileSystemStorageProvider(fileSystemStorageConfiguration));

            var shouldBeRedirectingToLocalStorageConfigPage =
                Assert.IsType<RedirectToActionResult>(await controller.Storage());
            Assert.Equal(nameof(StorageProvider), shouldBeRedirectingToLocalStorageConfigPage.ActionName);
            Assert.Equal(StorageProvider.FileSystem,
                shouldBeRedirectingToLocalStorageConfigPage.RouteValues["provider"]);


            await CanUploadRemoveFiles(controller);
        }

        internal static async Task CanUploadRemoveFiles(UIServerController controller)
        {
            var fileContent = "content";
            List<IFormFile> fileList = new List<IFormFile>();
            fileList.Add(TestUtils.GetFormFile("uploadtestfile1.txt", fileContent));

            var uploadFormFileResult = Assert.IsType<RedirectToActionResult>(await controller.CreateFiles(fileList));
            Assert.True(uploadFormFileResult.RouteValues.ContainsKey("fileIds"));
            string[] uploadFileList = (string[])uploadFormFileResult.RouteValues["fileIds"];
            var fileId = uploadFileList[0];
            Assert.Equal("Files", uploadFormFileResult.ActionName);

            //check if file was uploaded and saved in db
            var viewFilesViewModel =
                Assert.IsType<ViewFilesViewModel>(Assert.IsType<ViewResult>(await controller.Files(new string[] { fileId })).Model);

            Assert.NotEmpty(viewFilesViewModel.Files);
            Assert.True(viewFilesViewModel.DirectUrlByFiles.ContainsKey(fileId));
            Assert.NotEmpty(viewFilesViewModel.DirectUrlByFiles[fileId]);


            //verify file is available and the same
            using var net = new HttpClient();
            var data = await net.GetStringAsync(new Uri(viewFilesViewModel.DirectUrlByFiles[fileId]));
            Assert.Equal(fileContent, data);

            //create a temporary link to file
            var tmpLinkGenerate = Assert.IsType<RedirectToActionResult>(await controller.CreateTemporaryFileUrl(fileId,
                new UIServerController.CreateTemporaryFileUrlViewModel()
                {
                    IsDownload = true,
                    TimeAmount = 1,
                    TimeType = UIServerController.CreateTemporaryFileUrlViewModel.TmpFileTimeType.Minutes
                }));
            var statusMessageModel = controller.TempData.GetStatusMessageModel();
            Assert.NotNull(statusMessageModel);
            Assert.Equal(StatusMessageModel.StatusSeverity.Success, statusMessageModel.Severity);
            var index = statusMessageModel.Html.IndexOf("target='_blank'>");
            var url = statusMessageModel.Html.Substring(index)
                .Replace("</a>", string.Empty)
                .Replace("target='_blank'>", string.Empty);
            //verify tmpfile is available and the same
            data = await net.GetStringAsync(new Uri(url));
            Assert.Equal(fileContent, data);


            //delete file
            Assert.IsType<RedirectToActionResult>(await controller.DeleteFile(fileId));
            statusMessageModel = controller.TempData.GetStatusMessageModel();
            Assert.NotNull(statusMessageModel);

            Assert.Equal(StatusMessageModel.StatusSeverity.Success, statusMessageModel.Severity);

            //attempt to fetch deleted file
            viewFilesViewModel =
                Assert.IsType<ViewFilesViewModel>(Assert.IsType<ViewResult>(await controller.Files(new string[] { fileId })).Model);
            Assert.Null(viewFilesViewModel.DirectUrlByFiles);
        }
    }
}
