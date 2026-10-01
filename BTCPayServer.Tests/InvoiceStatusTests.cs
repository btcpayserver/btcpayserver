using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace BTCPayServer.Tests
{
    /// <summary>
    /// Tests privacy filtering and checkout compatibility for public invoice status responses.
    /// </summary>
    /// <param name="helper">The test output sink.</param>
    public class InvoiceStatusTests(ITestOutputHelper helper) : UnitTestBase(helper)
    {
        /// <summary>
        /// Verifies that status filtering removes sensitive model and extension-data fields while preserving
        /// payment methods, plugin settings, sounds, and tax information.
        /// </summary>
        [Fact]
        public void StatusResponsePreservesCheckoutSettingsAndRemovesSensitiveData()
        {
            var model = new CheckoutModel
            {
                InvoiceId = "invoice-id",
                Status = "New",
                CustomerEmail = "buyer@example.com",
                MerchantRefLink = "https://merchant.example/order?token=secret",
                OrderId = "private-order",
                ItemDesc = "private-description",
                StoreSupportUrl = "https://merchant.example/support/private-order",
                PaymentSoundUrl = "/checkout/payment.mp3",
                ErrorSoundUrl = "/checkout/error.mp3",
                NfcReadSoundUrl = "/checkout/nfcread.mp3",
                TaxIncluded = new() { Value = 1, Formatted = "$1.00" },
                AdditionalData = new()
                {
                    ["metadata"] = new JObject { ["customSecret"] = "private-extension" },
                    ["buyerEmail"] = "buyer@example.com",
                    ["BuyerAddress"] = "private-address",
                    ["merchantCheckoutLink"] = "https://merchant.example/order?token=secret",
                    ["nfcEnabled"] = true,
                    ["pluginSetting"] = new JObject { ["enabled"] = true }
                },
                AvailablePaymentMethods =
                [
                    new CheckoutModel.AvailablePaymentMethod
                    {
                        PaymentMethodId = PaymentMethodId.Parse("BTC-CHAIN"),
                        Displayed = true,
                        AdditionalData = new()
                        {
                            ["metadata"] = new JObject { ["secret"] = "private-method" },
                            ["buyerPhone"] = "private-phone",
                            ["customPaymentSetting"] = "enabled"
                        }
                    }
                ]
            };
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
            var response = JObject.FromObject(model.GetStatusResponse(), serializer);

            Assert.Equal("invoice-id", response["invoiceId"]);
            Assert.Equal("New", response["status"]);
            Assert.Equal("BTC-CHAIN", response["availablePaymentMethods"]![0]!["paymentMethodId"]);
            Assert.Null(response["availablePaymentMethods"]![0]!["metadata"]);
            Assert.Null(response["availablePaymentMethods"]![0]!["buyerPhone"]);
            Assert.Equal("enabled", response["availablePaymentMethods"]![0]!["customPaymentSetting"]);
            Assert.True(response["nfcEnabled"]!.Value<bool>());
            Assert.True(response["pluginSetting"]!["enabled"]!.Value<bool>());
            Assert.Equal("/checkout/payment.mp3", response["paymentSoundUrl"]);
            Assert.Equal("/checkout/error.mp3", response["errorSoundUrl"]);
            Assert.Equal("/checkout/nfcread.mp3", response["nfcReadSoundUrl"]);
            Assert.Equal(1m, response["taxIncluded"]!["value"]!.Value<decimal>());
            foreach (var field in new[] { "customerEmail", "buyerEmail", "BuyerAddress", "merchantRefLink", "merchantCheckoutLink", "orderId", "itemDesc", "storeSupportUrl", "metadata" })
                Assert.Null(response[field]);

            Assert.Null(JObject.FromObject(model, serializer)["customerEmail"]);
        }

        /// <summary>
        /// Verifies that public status routes omit private invoice metadata, preserve checkout data and stored metadata,
        /// and return not found for archived or missing invoices when accessed anonymously.
        /// </summary>
        [Fact(Timeout = TestUtils.TestTimeout)]
        [Trait("Integration", "Integration")]
        public async Task PublicStatusRoutesExcludeInvoiceMetadata()
        {
            using var tester = CreateServerTester(newDb: true);
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            await user.RegisterDerivationSchemeAsync("BTC");
            var client = await user.CreateClient();
            var metadata = new JObject
            {
                ["buyerEmail"] = "private-buyer@example.com",
                ["buyerName"] = "private-buyer-name",
                ["buyerAddress1"] = "private-address-one",
                ["buyerAddress2"] = "private-address-two",
                ["buyerPhone"] = "private-phone",
                ["orderId"] = "private-order-id",
                ["itemDesc"] = "private-item-description",
                ["taxIncluded"] = 0.001m,
                ["custom"] = new JObject { ["secret"] = "private-metadata" }
            };
            const string redirectUrl = "https://merchant.example/order?token=private-redirect";
            var invoice = await client.CreateInvoice(user.StoreId, new CreateInvoiceRequest
            {
                Amount = 0.01m,
                Currency = "BTC",
                Metadata = metadata,
                Checkout = new() { RedirectURL = redirectUrl }
            });

            foreach (var route in new[]
            {
                $"i/{invoice.Id}/status",
                $"i/{invoice.Id}/BTC-CHAIN/status",
                $"invoice/{invoice.Id}/status",
                $"invoice/{invoice.Id}/BTC-CHAIN/status",
                $"invoice/status?invoiceId={invoice.Id}",
                $"i/{invoice.Id}/status?paymentMethodId=BTC-CHAIN"
            })
            {
                var json = await tester.PayTester.HttpClient.GetStringAsync(route);
                var response = JObject.Parse(json);
                Assert.Equal(invoice.Id, response["invoiceId"]);
                Assert.Equal("New", response["status"]);
                Assert.Equal("BTC-CHAIN", response["paymentMethodId"]);
                Assert.False(string.IsNullOrEmpty(response["address"]?.Value<string>()));
                Assert.False(string.IsNullOrEmpty(response["invoiceBitcoinUrl"]?.Value<string>()));
                Assert.NotNull(response["due"]);
                Assert.NotNull(response["expirationSeconds"]);
                foreach (var property in metadata.Properties())
                {
                    if (property.Name != "taxIncluded")
                        Assert.Null(response[property.Name]);
                }
                Assert.Equal(0.001m, response["taxIncluded"]!["value"]!.Value<decimal>());
                Assert.NotNull(response["paymentSoundUrl"]);
                Assert.NotNull(response["nfcReadSoundUrl"]);
                Assert.NotNull(response["errorSoundUrl"]);
                Assert.NotNull(response["storeBranding"]);
                Assert.NotNull(response["redirectAutomatically"]);
                Assert.NotNull(response["receiptLink"]);
                Assert.Null(response["metadata"]);
                Assert.Null(response["customerEmail"]);
                Assert.Null(response["merchantRefLink"]);
                Assert.DoesNotContain("private-", json);
            }

            var storedInvoice = await client.GetInvoice(invoice.Id);
            Assert.True(JToken.DeepEquals(metadata, storedInvoice.Metadata));
            Assert.Equal(redirectUrl, storedInvoice.Checkout.RedirectURL);
            var checkout = await tester.PayTester.HttpClient.GetStringAsync($"i/{invoice.Id}");
            Assert.DoesNotContain("private-buyer@example.com", checkout);
            Assert.DoesNotContain("customerEmail", checkout);

            await client.ArchiveInvoice(invoice.Id);
            using var archivedResponse = await tester.PayTester.HttpClient.GetAsync($"i/{invoice.Id}/status");
            Assert.Equal(HttpStatusCode.NotFound, archivedResponse.StatusCode);
            using var missingResponse = await tester.PayTester.HttpClient.GetAsync("i/missing-invoice/status");
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        }
    }
}
