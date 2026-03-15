using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client.Models;
using BTCPayServer.Events;
using BTCPayServer.Plugins.Bitpay.Controllers;
using BTCPayServer.Plugins.Bitpay.Models;
using BTCPayServer.Plugins.Bitpay.Security;
using BTCPayServer.Plugins.Bitpay.Views;
using BTCPayServer.Views.Stores;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using NBitpayClient;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class BitpayTests(ITestOutputHelper log) : UnitTestBase(log)
{
       [Fact]
       [Trait("Integration", "Integration")]
        public async Task CanThrowBitpay404Error()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");

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

            try
            {
                await user.BitPay.GetInvoiceAsync(invoice.Id + "123");
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
            var err = await result.Content.ReadAsStringAsync();
            var errModel = JsonConvert.DeserializeObject<BitpayErrorsModel>(err);
            Assert.Equal("ApiKey authentication failed", errModel.Errors[0].Error);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUseServerInitiatedPairingCode()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();
            await acc.RegisterAsync();
            acc.CreateStore();

            var controller = acc.GetController<UIStoresTokenController>();
            var token = (RedirectToActionResult)await controller.CreateToken2(
                new CreateTokenViewModel()
                {
                    Label = "bla",
                    PublicKey = null,
                    StoreId = acc.StoreId
                });

            var pairingCode = (string)token.RouteValues!["pairingCode"];

            await acc.BitPay.AuthorizeClient(new PairingCode(pairingCode));
            Assert.True(await acc.BitPay.TestAccessAsync(Facade.Merchant));
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanSendIPN()
        {
            using var callbackServer = new CustomServer();
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();
            await acc.GrantAccessAsync();
            acc.RegisterDerivationScheme("BTC");
            await acc.ModifyGeneralSettings(p => p.SpeedPolicy = SpeedPolicy.LowSpeed);
            var invoice = await acc.BitPay.CreateInvoiceAsync(new Invoice
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
            var url = new BitcoinUrlBuilder(invoice.PaymentUrls.BIP21,
                tester.NetworkProvider.BTC.NBitcoinNetwork);
            var receivedPayment = false;
            var paid = false;
            var confirmed = false;
            var completed = false;
            while (!completed || !confirmed || !receivedPayment)
            {
                var request = await callbackServer.GetNextRequest();
                if (request.ContainsKey("event"))
                {
                    var evtName = request["event"]!.Value<string>("name");
                    switch (evtName)
                    {
                        case InvoiceEvent.Created:
                            await tester.ExplorerNode.SendToAddressAsync(url.Address!, url.Amount!);
                            break;
                        case InvoiceEvent.ReceivedPayment:
                            receivedPayment = true;
                            break;
                        case InvoiceEvent.PaidInFull:
                            // TODO, we should check that ReceivedPayment is sent after PaidInFull
                            // for now, we can't ensure this because the ReceivedPayment events isn't sent by the
                            // InvoiceWatcher, contrary to all other events
                            await tester.ExplorerNode.GenerateAsync(6);
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
                            Assert.Fail($"{evtName} was not expected");
                            break;
                    }
                }
            }
            var invoice2 = await acc.BitPay.GetInvoiceAsync(invoice.Id);
            Assert.NotNull(invoice2);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CantPairTwiceWithSamePubkey()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var acc = tester.NewAccount();
            await acc.RegisterAsync();
            acc.CreateStore();
            var store = acc.GetController<UIStoresTokenController>();
            var pairingCode = await acc.BitPay.RequestClientAuthorizationAsync("test", Facade.Merchant);
            Assert.IsType<RedirectToActionResult>(store.Pair(pairingCode.ToString(), acc.StoreId).GetAwaiter()
                .GetResult());

            pairingCode = await acc.BitPay.RequestClientAuthorizationAsync("test1", Facade.Merchant);
            acc.CreateStore();
            var store2 = acc.GetController<UIStoresTokenController>();
            await store2.Pair(pairingCode.ToString(), store2.CurrentStore.Id);
            Assert.Contains(nameof(PairingResult.ReusedKey),
                store2.TempData[WellKnownTempData.ErrorMessage].ToString(), StringComparison.CurrentCultureIgnoreCase);
        }


        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CheckCORSSetOnBitpayAPI()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            foreach (var req in new[] { "invoices/", "invoices", "rates", "tokens" }.Select(async path =>
              {
                  using var client = new HttpClient();
                  var message = new HttpRequestMessage(HttpMethod.Options,
                          tester.PayTester.ServerUri.AbsoluteUri + path);
                  message.Headers.Add("Access-Control-Request-Headers", "test");
                  message.Headers.TryAddWithoutValidation("Origin", "https://test.com");
                  message.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
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


            var client2 = new HttpClient();
            var message2 = new HttpRequestMessage(HttpMethod.Options,
                tester.PayTester.ServerUri.AbsoluteUri + "rates");
            message2.Headers.TryAddWithoutValidation("Origin", "https://test.com");
            message2.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
            var response2 = await client2.SendAsync(message2);
            response2.EnsureSuccessStatusCode();
            Assert.True(response2.Headers.TryGetValues("Access-Control-Allow-Origin", out var val2));
            Assert.Equal("*", val2.FirstOrDefault());
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task TestAccessBitpayAPI()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            Assert.False(await user.BitPay.TestAccessAsync(Facade.Merchant));
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");

            Assert.True(await user.BitPay.TestAccessAsync(Facade.Merchant));

            // Test request pairing code client side
            var storeController = user.GetController<UIStoresTokenController>();
            await storeController
                .CreateToken(user.StoreId, new CreateTokenViewModel() { Label = "test2", StoreId = user.StoreId });
            Assert.NotNull(storeController.GeneratedPairingCode);


            var k = new Key();
            var bitpay = new Bitpay(k, tester.PayTester.ServerUri);
            bitpay.AuthorizeClient(new PairingCode(storeController.GeneratedPairingCode)).Wait();
            Assert.True(await bitpay.TestAccessAsync(Facade.Merchant));
            Assert.True(await bitpay.TestAccessAsync(Facade.PointOfSale));
            // Same with a new instance
            bitpay = new Bitpay(k, tester.PayTester.ServerUri);
            Assert.True(await bitpay.TestAccessAsync(Facade.Merchant));
            Assert.True(await bitpay.TestAccessAsync(Facade.PointOfSale));
            var client = new HttpClient();
            var token = (await bitpay.GetAccessTokenAsync(Facade.Merchant)).Value;
            var getRates = tester.PayTester.ServerUri.AbsoluteUri + $"rates/?cryptoCode=BTC&token={token}";
            var req = new HttpRequestMessage(HttpMethod.Get, getRates);
            req.Headers.Add("x-signature", NBitpayClient.Extensions.BitIdExtensions.GetBitIDSignature(k, getRates, null));
            req.Headers.Add("x-identity", k.PubKey.ToHex());
            var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            // Can generate API Key
            var repo = tester.PayTester.GetService<TokenRepository>();
            Assert.Empty(await repo.GetLegacyAPIKeys(user.StoreId));
            Assert.IsType<RedirectToActionResult>(await user.GetController<UIStoresTokenController>()
                .GenerateAPIKey(user.StoreId));

            var apiKey = Assert.Single(await repo.GetLegacyAPIKeys(user.StoreId));
            ///////

            // Generating a new one remove the previous
            Assert.IsType<RedirectToActionResult>(await user.GetController<UIStoresTokenController>()
                .GenerateAPIKey(user.StoreId));
            var apiKey2 = Assert.Single(await repo.GetLegacyAPIKeys(user.StoreId));
            Assert.NotEqual(apiKey, apiKey2);
            ////////

            apiKey = apiKey2;

            // Can create an invoice with this new API Key
            var message = new HttpRequestMessage(HttpMethod.Post,
                tester.PayTester.ServerUri.AbsoluteUri + "invoices");
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Encoders.Base64.EncodeData(Encoders.ASCII.DecodeData(apiKey)));
            var invoice = new Invoice() { Price = 5000.0m, Currency = "USD" };
            message.Content = new StringContent(JsonConvert.SerializeObject(invoice), Encoding.UTF8,
                "application/json");
            var result = await client.SendAsync(message);
            result.EnsureSuccessStatusCode();
            /////////////////////

            // Have error 403 with a bad signature
            client = new HttpClient();
            var mess =
                new HttpRequestMessage(HttpMethod.Get, tester.PayTester.ServerUri.AbsoluteUri + "tokens");
            mess.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
            mess.Headers.Add("x-signature",
                "3045022100caa123193afc22ef93d9c6b358debce6897c09dd9869fe6fe029c9cb43623fac022000b90c65c50ba8bbbc6ebee8878abe5659e17b9f2e1b27d95eda4423da5608fe");
            mess.Headers.Add("x-identity",
                "04b4d82095947262dd70f94c0a0e005ec3916e3f5f2181c176b8b22a52db22a8c436c4703f43a9e8884104854a11e1eb30df8fdf116e283807a1f1b8fe4c182b99");
            mess.Method = HttpMethod.Get;
            result = await client.SendAsync(mess);
            Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);

            //
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUseAnyoneCanCreateInvoice()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
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

        [Fact]
        [Trait("Playwright", "Playwright-2")]
        public async Task CanUsePairing()
        {
            await using var s = CreatePlaywrightTester();
            await s.StartAsync();
            await s.Page.GotoAsync(s.Link("/api-access-request"));
            Assert.Contains("ReturnUrl", s.Page.Url);
            await s.GoToRegister();
            await s.RegisterNewUser();
            await s.CreateNewStore();
            await s.AddDerivationScheme();

            await s.GoToStore(s.StoreId, StoreNavPages.Tokens);
            await s.Page.Locator("#CreateNewToken").ClickAsync();
            await s.ClickPagePrimary();
            var url = s.Page.Url;
            var pairingCode = Regex.Match(new Uri(url, UriKind.Absolute).Query, "pairingCode=([^&]*)").Groups[1].Value;

            await s.ClickPagePrimary();
            await s.FindAlertMessage();
            Assert.Contains(pairingCode, await s.Page.ContentAsync());

            var client = new Bitpay(new Key(), s.ServerUri);
            await client.AuthorizeClient(new PairingCode(pairingCode));
            await client.CreateInvoiceAsync(
                new Invoice { Price = 1.000000012m, Currency = "USD", FullNotifications = true },
                Facade.Merchant);

            client = new Bitpay(new Key(), s.ServerUri);

            var code = await client.RequestClientAuthorizationAsync("hehe", Facade.Merchant);
            await s.Page.GotoAsync(code.CreateLink(s.ServerUri).ToString());
            await s.ClickPagePrimary();

            await client.CreateInvoiceAsync(
                new Invoice { Price = 1.000000012m, Currency = "USD", FullNotifications = true },
                Facade.Merchant);

            await s.Page.GotoAsync(s.Link("/api-tokens"));
            await s.ClickPagePrimary(); // Request
            await s.ClickPagePrimary(); // Approve
            var url2 = s.Page.Url;
            var pairingCode2 = Regex.Match(new Uri(url2, UriKind.Absolute).Query, "pairingCode=([^&]*)").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(pairingCode2));
        }
}
