using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
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
        /// Verifies that status filtering removes sensitive checkout fields while preserving
        /// payment methods, plugin extension data, sounds, and tax information.
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
                ItemDesc = "Product description",
                StoreSupportUrl = "https://merchant.example/support",
                PaymentSoundUrl = "/checkout/payment.mp3",
                ErrorSoundUrl = "/checkout/error.mp3",
                NfcReadSoundUrl = "/checkout/nfcread.mp3",
                NfcEnabled = true,
                TaxIncluded = new() { Value = 1, Formatted = "$1.00" },
                AdditionalData = new()
                {
                    ["metadata"] = new JObject { ["pluginMode"] = "checkout" },
                    ["customerDisplayMode"] = "compact",
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
                            ["metadata"] = new JObject { ["label"] = "plugin-label" },
                            ["buyerCurrency"] = "BTC",
                            ["customPaymentSetting"] = "enabled"
                        }
                    }
                ]
            };
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
            var checkoutExtensionData = JObject.FromObject(model.AdditionalData, serializer);
            var paymentMethodExtensionData = JObject.FromObject(model.AvailablePaymentMethods[0].AdditionalData, serializer);
            var response = JObject.FromObject(model.GetStatusResponse(), serializer);

            Assert.Equal("invoice-id", response["invoiceId"]);
            Assert.Equal("New", response["status"]);
            Assert.Equal("Product description", response["itemDesc"]);
            Assert.Equal("https://merchant.example/support", response["storeSupportUrl"]);
            Assert.Equal("BTC-CHAIN", response["availablePaymentMethods"]![0]!["paymentMethodId"]);
            Assert.True(JToken.DeepEquals(checkoutExtensionData, JObject.FromObject(model.AdditionalData, serializer)));
            Assert.True(JToken.DeepEquals(paymentMethodExtensionData, JObject.FromObject(model.AvailablePaymentMethods[0].AdditionalData, serializer)));
            Assert.Equal("plugin-label", response["availablePaymentMethods"]![0]!["metadata"]!["label"]);
            Assert.Equal("BTC", response["availablePaymentMethods"]![0]!["buyerCurrency"]);
            Assert.Equal("enabled", response["availablePaymentMethods"]![0]!["customPaymentSetting"]);
            Assert.Equal("checkout", response["metadata"]!["pluginMode"]);
            Assert.Equal("compact", response["customerDisplayMode"]);
            Assert.True(response["nfcEnabled"]!.Value<bool>());
            Assert.True(response["pluginSetting"]!["enabled"]!.Value<bool>());
            Assert.Equal("/checkout/payment.mp3", response["paymentSoundUrl"]);
            Assert.Equal("/checkout/error.mp3", response["errorSoundUrl"]);
            Assert.Equal("/checkout/nfcread.mp3", response["nfcReadSoundUrl"]);
            Assert.Equal(1m, response["taxIncluded"]!["value"]!.Value<decimal>());
            foreach (var field in new[] { "customerEmail", "merchantRefLink", "orderId" })
                Assert.Null(response[field]);

            Assert.Null(JObject.FromObject(model, serializer)["customerEmail"]);
        }

        /// <summary>
        /// Verifies that arbitrary invoice metadata is absent before status filtering, public status routes omit sensitive
        /// checkout fields, and archived or missing invoices are inaccessible when accessed anonymously.
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
                ["buyerAddress1"] = "private-buyer-address-one",
                ["buyerAddress2"] = "private-buyer-address-two",
                ["buyerPhone"] = "private-buyer-phone",
                ["buyerCity"] = "private-buyer-city",
                ["buyerState"] = "private-buyer-state",
                ["buyerZip"] = "private-buyer-zip",
                ["buyerCountry"] = "private-buyer-country",
                ["buyerAddress"] = "private-buyer-address",
                ["customerAddress"] = "private-buyer-customer-address",
                ["orderId"] = "private-order-id",
                ["itemDesc"] = "Product description",
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

            var controller = tester.PayTester.GetController<UIInvoiceController>();
            var checkoutModel = (await controller.Checkout(invoice.Id)).AssertViewModel<CheckoutModel>();
            var unfilteredResponse = JObject.FromObject(checkoutModel,
                JsonSerializer.Create(tester.PayTester.GetService<JsonSerializerSettings>()));
            Assert.Equal("private-order-id", unfilteredResponse["orderId"]);
            Assert.Equal(redirectUrl, unfilteredResponse["merchantRefLink"]);
            Assert.Equal("Product description", unfilteredResponse["itemDesc"]);
            Assert.Null(unfilteredResponse["metadata"]);
            Assert.Null(unfilteredResponse["custom"]);

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
                    if (property.Name is not ("taxIncluded" or "itemDesc"))
                        Assert.Null(response[property.Name]);
                }
                Assert.Equal("Product description", response["itemDesc"]);
                Assert.NotNull(response.Property("storeSupportUrl"));
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
            Assert.Contains(redirectUrl, checkout);
            Assert.Contains("private-order-id", checkout);
            Assert.Contains("Product description", checkout);
            Assert.DoesNotContain("private-buyer", checkout);
            Assert.DoesNotContain("private-metadata", checkout);
            Assert.DoesNotContain("customerEmail", checkout);

            await client.ArchiveInvoice(invoice.Id);
            using var archivedResponse = await tester.PayTester.HttpClient.GetAsync($"i/{invoice.Id}/status");
            Assert.Equal(HttpStatusCode.NotFound, archivedResponse.StatusCode);
            using var missingResponse = await tester.PayTester.HttpClient.GetAsync("i/missing-invoice/status");
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        }

        /// <summary>
        /// Verifies that flat and nested legacy buyer information is stored without being exposed
        /// through public status routes or normal and modal checkout HTML.
        /// </summary>
        [Theory(Timeout = TestUtils.TestTimeout)]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Integration", "Integration")]
        public async Task PublicCheckoutExcludesLegacyBuyerInformation(bool nestedBuyer)
        {
            using var tester = CreateServerTester(newDb: true);
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            await user.RegisterDerivationSchemeAsync("BTC");
            var buyer = new NBitpayClient.Buyer
            {
                Name = "private-legacy-name",
                Address1 = "private-legacy-address-one",
                Address2 = "private-legacy-address-two",
                City = "private-legacy-city",
                State = "private-legacy-state",
                zip = "private-legacy-zip",
                country = "private-legacy-country",
                email = "private-legacy-buyer@example.com",
                phone = "private-legacy-phone"
            };
            var posData = new JObject
            {
                ["buyerAddress"] = "private-legacy-buyer-address",
                ["customerAddress"] = "private-legacy-customer-address",
                ["custom"] = new JObject { ["secret"] = "private-legacy-metadata" }
            };
            var request = new NBitpayClient.Invoice
            {
                Price = 0.01m,
                Currency = "BTC",
                ItemDesc = "Product description",
                PosData = posData.ToString(Formatting.None),
                NotificationEmail = "private-legacy-notification@example.com"
            };
            if (nestedBuyer)
                request.Buyer = buyer;
            else
            {
                request.BuyerName = buyer.Name;
                request.BuyerAddress1 = buyer.Address1;
                request.BuyerAddress2 = buyer.Address2;
                request.BuyerCity = buyer.City;
                request.BuyerState = buyer.State;
                request.BuyerZip = buyer.zip;
                request.BuyerCountry = buyer.country;
                request.BuyerEmail = buyer.email;
                request.BuyerPhone = buyer.phone;
            }

            var invoice = await user.BitPay.CreateInvoiceAsync(request);
            var client = await user.CreateClient();
            var storedInvoice = await client.GetInvoice(invoice.Id);
            var storedMetadata = InvoiceMetadata.FromJObject(storedInvoice.Metadata);
            Assert.Equal(buyer.Name, storedMetadata.BuyerName);
            Assert.Equal(buyer.Address1, storedMetadata.BuyerAddress1);
            Assert.Equal(buyer.Address2, storedMetadata.BuyerAddress2);
            Assert.Equal(buyer.City, storedMetadata.BuyerCity);
            Assert.Equal(buyer.State, storedMetadata.BuyerState);
            Assert.Equal(buyer.zip, storedMetadata.BuyerZip);
            Assert.Equal(buyer.country, storedMetadata.BuyerCountry);
            Assert.Equal(buyer.email, storedMetadata.BuyerEmail);
            Assert.Equal(buyer.phone, storedMetadata.BuyerPhone);
            Assert.True(JToken.DeepEquals(posData, storedMetadata.PosData));

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
                Assert.Equal("Product description", response["itemDesc"]);
                Assert.False(string.IsNullOrEmpty(response["address"]?.Value<string>()));
                Assert.Null(response["buyer"]);
                Assert.Null(response["buyerAddress"]);
                Assert.Null(response["customerAddress"]);
                Assert.Null(response["customerEmail"]);
                Assert.Null(response["posData"]);
                Assert.DoesNotContain("private-legacy-", json);
            }

            foreach (var view in new[] { "", "?view=modal" })
            {
                var checkout = await tester.PayTester.HttpClient.GetStringAsync($"i/{invoice.Id}{view}");
                Assert.DoesNotContain("private-legacy-", checkout);
                Assert.DoesNotContain("customerEmail", checkout);
            }
        }
    }
}
