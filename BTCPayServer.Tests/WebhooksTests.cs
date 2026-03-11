using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Webhooks.HostedServices;
using BTCPayServer.Views.Stores;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using static Microsoft.Playwright.Assertions;
// ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local

namespace BTCPayServer.Tests;

public class WebhooksTests(ITestOutputHelper log) : UnitTestBase(log)
{
    [Fact]
    [Trait("Playwright", "Playwright-2")]
    public async Task CanUseWebhooks()
    {
        await using var s = CreatePlaywrightTester();
        await s.StartAsync();
        await s.RegisterNewUser(true);
        await s.CreateNewStore();
        await s.GoToStore(StoreNavPages.Webhooks);

        TestLogs.LogInformation("Let's create two webhooks");
        for (var i = 0; i < 2; i++)
        {
            await s.ClickPagePrimary();
            await s.Page.FillAsync("[name='PayloadUrl']", $"http://127.0.0.1/callback{i}");
            await s.Page.SelectOptionAsync("#Everything", "false");
            await s.Page.ClickAsync("#InvoiceCreated");
            await s.Page.ClickAsync("#InvoiceProcessing");
            await s.ClickPagePrimary();
        }

        await s.FindAlertMessage();
        TestLogs.LogInformation("Let's delete one of them");
        var deleteLinks = await s.Page.Locator("a:has-text('Delete')").AllAsync();
        Assert.Equal(2, deleteLinks.Count);
        await deleteLinks[0].ClickAsync();
        await s.ConfirmDeleteModal();
        deleteLinks = await s.Page.Locator("a:has-text('Delete')").AllAsync();
        Assert.Single(deleteLinks);
        await s.FindAlertMessage();

        TestLogs.LogInformation("Let's try to update one of them");
        await s.Page.ClickAsync("text=Modify");

        using var server = new FakeServer();
        await server.Start();
        await s.Page.FillAsync("[name='PayloadUrl']", server.ServerUri.AbsoluteUri);
        await s.Page.FillAsync("[name='Secret']", "HelloWorld");
        await s.Page.ClickAsync("[name='update']");
        await s.FindAlertMessage();
        await s.Page.ClickAsync("text=Modify");

        // Check which events are selected
        await Expect(s.Page.Locator("input[value='InvoiceProcessing']")).ToBeCheckedAsync();
        await Expect(s.Page.Locator("input[value='InvoiceCreated']")).ToBeCheckedAsync();
        await Expect(s.Page.Locator("input[value='InvoiceReceivedPayment']")).Not.ToBeCheckedAsync();

        await s.Page.ClickAsync("[name='update']");
        await s.FindAlertMessage();
        var pageContent = await s.Page.ContentAsync();
        Assert.Contains(server.ServerUri.AbsoluteUri, pageContent);

        TestLogs.LogInformation("Let's see if we can generate an event");
        await s.GoToStore();
        await s.AddDerivationScheme();
        await s.CreateInvoice();
        var request = await server.GetNextRequest();
        var headers = request.Request.Headers;
        var actualSig = headers["BTCPay-Sig"].First();
        var bytes = await request.Request.Body.ReadBytesAsync((int)headers.ContentLength!.Value);
        var expectedSig =
            $"sha256={Encoders.Hex.EncodeData(NBitcoin.Crypto.Hashes.HMACSHA256(Encoding.UTF8.GetBytes("HelloWorld"), bytes))}";
        Assert.Equal(expectedSig, actualSig);
        request.Response.StatusCode = 200;
        server.Done();

        TestLogs.LogInformation("Let's make a failed event");
        var invoiceId = await s.CreateInvoice();
        request = await server.GetNextRequest();
        request.Response.StatusCode = 404;
        server.Done();

        // The delivery is done asynchronously, so small wait here
        await Task.Delay(500);
        await s.GoToStore();
        await s.GoToStore(StoreNavPages.Webhooks);
        await s.Page.ClickAsync("text=Modify");
        var redeliverElements = await s.Page.Locator("button.redeliver").AllAsync();

        // One worked, one failed
        await s.Page.Locator(".icon-cross").WaitForAsync();
        await s.Page.Locator(".icon-checkmark").WaitForAsync();
        await redeliverElements[0].ClickAsync();

        await s.FindAlertMessage();
        request = await server.GetNextRequest();
        request.Response.StatusCode = 404;
        server.Done();

        TestLogs.LogInformation("Can we browse the json content?");
        await CanBrowseContentAsync(s);

        await s.GoToInvoices();
        await s.Page.ClickAsync($"text={invoiceId}");
        await CanBrowseContentAsync(s);
        var redeliverElement = s.Page.Locator("button.redeliver").First;
        await redeliverElement.ClickAsync();

        await s.FindAlertMessage();
        request = await server.GetNextRequest();
        request.Response.StatusCode = 404;
        server.Done();

        TestLogs.LogInformation("Let's see if we can delete store with some webhooks inside");
        await s.GoToStore();
        await s.Page.ClickAsync("#DeleteStore");
        await s.ConfirmDeleteModal();
        await s.FindAlertMessage();
    }

    private static async Task CanBrowseContentAsync(PlaywrightTester s)
    {
        var newPageDoing = s.Page.Context.WaitForPageAsync();
        await s.Page.ClickAsync(".delivery-content");
        var newPage = await newPageDoing;
        var bodyText = await newPage.Locator("body").TextContentAsync();
        JObject.Parse(bodyText!);
        await newPage.CloseAsync();
    }

    [Fact]
    [Trait("Fast", "Fast")]
    public void CanFixupWebhookEventPropertyName()
    {
        var legacy = "{\"orignalDeliveryId\":\"blahblah\"}";
        var obj = JsonConvert.DeserializeObject<WebhookEvent>(legacy, WebhookEvent.DefaultSerializerSettings);
        Assert.Equal("blahblah", obj.OriginalDeliveryId);
        var serialized = JsonConvert.SerializeObject(obj, WebhookEvent.DefaultSerializerSettings);
        Assert.DoesNotContain("orignalDeliveryId", serialized);
        Assert.Contains("originalDeliveryId", serialized);
    }

     [Fact()]
        [Trait("Integration", "Integration")]
        public async Task EnsureWebhooksInvoiceExpiredPaidLatePartial()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            await user.SetupWebhook();
            var client = await user.CreateClient();

            // PaidAfterExpiration
            var invoicePaidAfterExpiration = await client.CreateInvoice(user.StoreId, new CreateInvoiceRequest() { Amount = 0.01m, Currency = "BTC" });
            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceCreated, (WebhookInvoiceEvent x) => Assert.Equal(invoicePaidAfterExpiration.Id, x.InvoiceId));

            await tester.PayTester.InvoiceRepository.UpdateInvoiceExpiry(invoicePaidAfterExpiration.Id, TimeSpan.FromSeconds(0));
            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceExpired, (WebhookInvoiceEvent x) => Assert.Equal(invoicePaidAfterExpiration.Id, x.InvoiceId));

            var inv = new BitcoinUrlBuilder((await client.GetInvoicePaymentMethods(user.StoreId, invoicePaidAfterExpiration.Id)).Single(model =>
                    PaymentMethodId.Parse(model.PaymentMethodId) ==
                    PaymentTypes.CHAIN.GetPaymentMethodId("BTC"))
                .PaymentLink, tester.ExplorerNode.Network);
            await tester.ExplorerNode.SendToAddressAsync(inv.Address!, Money.Coins(inv.Amount!.ToDecimal(MoneyUnit.BTC)));
            await Task.Delay(1000);

            await user.AssertHasWebhookEvent(WebhookEventType.InvoicePaidAfterExpiration, (WebhookInvoiceEvent evt) =>
            {
                Assert.Equal(invoicePaidAfterExpiration.Id, evt.InvoiceId);
            });

            // ExpiredPaidPartial
            var invoiceExpiredPartial = await client.CreateInvoice(user.StoreId, new CreateInvoiceRequest()
            {
                Amount = 0.01m, Currency = "BTC", Checkout = new InvoiceDataBase.CheckoutOptions {
                    Expiration = TimeSpan.FromMinutes(1)
                }
            });
            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceCreated, (WebhookInvoiceEvent x) => Assert.Equal(invoiceExpiredPartial.Id, x.InvoiceId));

            inv = new BitcoinUrlBuilder((await client.GetInvoicePaymentMethods(user.StoreId, invoiceExpiredPartial.Id)).Single(model =>
                    PaymentMethodId.Parse(model.PaymentMethodId) ==
                    PaymentTypes.CHAIN.GetPaymentMethodId("BTC"))
                .PaymentLink, tester.ExplorerNode.Network);
            await tester.ExplorerNode.SendToAddressAsync(inv.Address!, Money.Coins(inv.Amount!.ToDecimal(MoneyUnit.BTC)/2m));

            await tester.PayTester.InvoiceRepository.UpdateInvoiceExpiry(invoiceExpiredPartial.Id, TimeSpan.FromSeconds(2));
            await Task.Delay(1000); // give it time to expire and process payments

            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceExpired, (WebhookInvoiceEvent x) => Assert.Equal(invoiceExpiredPartial.Id, x.InvoiceId));
            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceExpiredPaidPartial, (WebhookInvoiceEvent evt) =>
            {
                Assert.Equal(invoiceExpiredPartial.Id, evt.InvoiceId);
            });
        }


        [Fact()]
        [Trait("Integration", "Integration")]
        public async Task EnsureWebhooksTrigger()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            await user.SetupWebhook();
            var client = await user.CreateClient();


           var  invoice = await client.CreateInvoice(user.StoreId, new CreateInvoiceRequest()
            {
                Amount = 0.00m,
                Currency = "BTC"
            });
            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceCreated,  (WebhookInvoiceEvent x)=> Assert.Equal(invoice.Id, x.InvoiceId));

            //invoice payment webhooks
            invoice = await client.CreateInvoice(user.StoreId, new CreateInvoiceRequest()
            {
                Amount = 0.01m,
                Currency = "BTC"
            });

            var invoicePaymentRequest = new BitcoinUrlBuilder((await client.GetInvoicePaymentMethods(user.StoreId, invoice.Id)).Single(model =>
                    PaymentMethodId.Parse(model.PaymentMethodId) ==
                    PaymentTypes.CHAIN.GetPaymentMethodId("BTC"))
                .PaymentLink, tester.ExplorerNode.Network);
            var halfPaymentTx = await tester.ExplorerNode.SendToAddressAsync(invoicePaymentRequest.Address!, Money.Coins(invoicePaymentRequest.Amount!.ToDecimal(MoneyUnit.BTC)/2m));

            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceCreated,  (WebhookInvoiceEvent x)=> Assert.Equal(invoice.Id, x.InvoiceId));
            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceReceivedPayment,
                (WebhookInvoiceReceivedPaymentEvent x) =>
                {
                    Assert.Equal(invoice.Id, x.InvoiceId);
                    Assert.Contains(halfPaymentTx.ToString(), x.Payment.Id);
                });
            invoicePaymentRequest = new BitcoinUrlBuilder((await client.GetInvoicePaymentMethods(user.StoreId, invoice.Id)).Single(model =>
                    PaymentMethodId.Parse(model.PaymentMethodId) ==
                    PaymentTypes.CHAIN.GetPaymentMethodId("BTC"))
                            .PaymentLink, tester.ExplorerNode.Network);
            var remainingPaymentTx = await tester.ExplorerNode.SendToAddressAsync(invoicePaymentRequest.Address!, Money.Coins(invoicePaymentRequest.Amount!.ToDecimal(MoneyUnit.BTC)));
            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceReceivedPayment,
                (WebhookInvoiceReceivedPaymentEvent x) =>
                {
                    Assert.Equal(invoice.Id, x.InvoiceId);
                    Assert.Contains(remainingPaymentTx.ToString(), x.Payment.Id);
                });
            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceProcessing, (WebhookInvoiceEvent x) => Assert.Equal(invoice.Id, x.InvoiceId));
            await tester.ExplorerNode.GenerateAsync(1);

            await  user.AssertHasWebhookEvent(WebhookEventType.InvoicePaymentSettled,
                (WebhookInvoiceReceivedPaymentEvent x) =>
                {
                    Assert.Equal(invoice.Id, x.InvoiceId);
                    Assert.Contains(halfPaymentTx.ToString(), x.Payment.Id);
                });
            await  user.AssertHasWebhookEvent(WebhookEventType.InvoicePaymentSettled,
                (WebhookInvoiceReceivedPaymentEvent x) =>
                {
                    Assert.Equal(invoice.Id, x.InvoiceId);
                    Assert.Contains(remainingPaymentTx.ToString(), x.Payment.Id);
                });

            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceSettled,  (WebhookInvoiceEvent x)=> Assert.Equal(invoice.Id, x.InvoiceId));

            invoice = await client.CreateInvoice(user.StoreId, new CreateInvoiceRequest()
            {
                Amount = 0.01m,
                Currency = "BTC",
            });
            invoicePaymentRequest = new BitcoinUrlBuilder((await client.GetInvoicePaymentMethods(user.StoreId, invoice.Id)).Single(model =>
                    PaymentMethodId.Parse(model.PaymentMethodId) ==
                    PaymentTypes.CHAIN.GetPaymentMethodId("BTC"))
                .PaymentLink, tester.ExplorerNode.Network);
            halfPaymentTx =  await tester.ExplorerNode.SendToAddressAsync(invoicePaymentRequest.Address!, Money.Coins(invoicePaymentRequest.Amount!.ToDecimal(MoneyUnit.BTC)/2m));


            await  user.AssertHasWebhookEvent(WebhookEventType.InvoiceCreated,  (WebhookInvoiceEvent x)=> Assert.Equal(invoice.Id, x.InvoiceId));
            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceReceivedPayment,
                (WebhookInvoiceReceivedPaymentEvent x) =>
                {
                    Assert.Equal(invoice.Id, x.InvoiceId);
                    Assert.Contains(halfPaymentTx.ToString(), x.Payment.Id);
                });

            invoice = await client.CreateInvoice(user.StoreId, new CreateInvoiceRequest()
            {
                Amount = 0.01m,
                Currency = "BTC"
            });

            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceCreated,  (WebhookInvoiceEvent x)=> Assert.Equal(invoice.Id, x.InvoiceId));
            await client.MarkInvoiceStatus(user.StoreId, invoice.Id, new MarkInvoiceStatusRequest() { Status = InvoiceStatus.Invalid});
            await user.AssertHasWebhookEvent(WebhookEventType.InvoiceInvalid,  (WebhookInvoiceEvent x)=> Assert.Equal(invoice.Id, x.InvoiceId));

            //payment request webhook test
            var pr = await client.CreatePaymentRequest(user.StoreId, new ()
            {
                Amount = 100m,
                Currency = "USD",
                Title = "test pr",
                //TODO: this is a bug, we should not have these props in create request
                StoreId = user.StoreId,
                FormResponse = new JObject(),
                //END todo
                Description = "lala baba"
            });
            await user.AssertHasWebhookEvent(WebhookEventType.PaymentRequestCreated,  (WebhookPaymentRequestEvent x)=> Assert.Equal(pr.Id, x.PaymentRequestId));
            pr = await client.UpdatePaymentRequest(user.StoreId, pr.Id,
                new() { Title = "test pr updated", Amount = 100m,
                    Currency = "USD",
                    //TODO: this is a bug, we should not have these props in create request
                    StoreId = user.StoreId,
                    FormResponse = new JObject(),
                    //END todo
                    Description = "lala baba"});
            await user.AssertHasWebhookEvent(WebhookEventType.PaymentRequestUpdated,  (WebhookPaymentRequestEvent x)=> Assert.Equal(pr.Id, x.PaymentRequestId));
            var inv = await client.PayPaymentRequest(user.StoreId, pr.Id, new PayPaymentRequestRequest());

            await client.MarkInvoiceStatus(user.StoreId, inv.Id, new MarkInvoiceStatusRequest() { Status = InvoiceStatus.Settled});
            await user.AssertHasWebhookEvent(WebhookEventType.PaymentRequestStatusChanged,  (WebhookPaymentRequestEvent x)=>
            {
                Assert.Equal(PaymentRequestStatus.Completed, x.Status);
                Assert.Equal(pr.Id, x.PaymentRequestId);
            });
            await client.ArchivePaymentRequest(user.StoreId, pr.Id);
            await user.AssertHasWebhookEvent(WebhookEventType.PaymentRequestArchived,  (WebhookPaymentRequestEvent x)=> Assert.Equal(pr.Id, x.PaymentRequestId));
            //payoyt webhooks test
            var payout = await client.CreatePayout(user.StoreId,
                new CreatePayoutThroughStoreRequest()
                {
                    Amount = 0.0001m,
                    Destination = (await tester.ExplorerNode.GetNewAddressAsync()).ToString(),
                    Approved = true,
                    PayoutMethodId = "BTC"
                });
            await user.AssertHasWebhookEvent(WebhookEventType.PayoutCreated,  (WebhookPayoutEvent x)=> Assert.Equal(payout.Id, x.PayoutId));
             await client.MarkPayout(user.StoreId, payout.Id, new MarkPayoutRequest(){ State = PayoutState.AwaitingApproval});
             await user.AssertHasWebhookEvent(WebhookEventType.PayoutUpdated,  (WebhookPayoutEvent x)=>
             {
                 Assert.Equal(payout.Id, x.PayoutId);
                 Assert.Equal(PayoutState.AwaitingApproval, x.PayoutState);
             });

             await client.ApprovePayout(user.StoreId, payout.Id, new ApprovePayoutRequest());
             await user.AssertHasWebhookEvent(WebhookEventType.PayoutApproved,  (WebhookPayoutEvent x)=>
             {
                 Assert.Equal(payout.Id, x.PayoutId);
                 Assert.Equal(PayoutState.AwaitingPayment, x.PayoutState);
             });
             await client.CancelPayout(user.StoreId, payout.Id );
             await  user.AssertHasWebhookEvent(WebhookEventType.PayoutUpdated,  (WebhookPayoutEvent x)=>
             {
                 Assert.Equal(payout.Id, x.PayoutId);
                 Assert.Equal(PayoutState.Cancelled, x.PayoutState);
             });
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanUseWebhooks2()
        {
            void AssertHook(FakeServer fakeServer, StoreWebhookData hook)
            {
                Assert.True(hook.Enabled);
                Assert.True(hook.AuthorizedEvents.Everything);
                Assert.False(hook.AutomaticRedelivery);
                Assert.Equal(fakeServer.ServerUri.AbsoluteUri, hook.Url);
            }
            using var tester = CreateServerTester(newDb: true);
            using var fakeServer = new FakeServer();
            await fakeServer.Start();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");
            var clientProfile = await user.CreateClient(Policies.CanModifyWebhooks, Policies.CanCreateInvoice);
            var hook = await clientProfile.CreateWebhook(user.StoreId, new CreateStoreWebhookRequest()
            {
                Url = fakeServer.ServerUri.AbsoluteUri,
                AutomaticRedelivery = false
            });
            Assert.NotNull(hook.Secret);
            AssertHook(fakeServer, hook);
            hook = await clientProfile.GetWebhook(user.StoreId, hook.Id);
            AssertHook(fakeServer, hook);
            var hooks = await clientProfile.GetWebhooks(user.StoreId);
            hook = Assert.Single(hooks);
            AssertHook(fakeServer, hook);
            await clientProfile.CreateInvoice(user.StoreId,
                        new CreateInvoiceRequest() { Currency = "USD", Amount = 100 });
            var req = await fakeServer.GetNextRequest();
            req.Response.StatusCode = 200;
            fakeServer.Done();
            hook = await clientProfile.UpdateWebhook(user.StoreId, hook.Id, new UpdateStoreWebhookRequest()
            {
                Url = hook.Url,
                Secret = "lol",
                AutomaticRedelivery = false
            });
            Assert.Null(hook.Secret);
            AssertHook(fakeServer, hook);
            WebhookDeliveryData delivery = null;
            await TestUtils.EventuallyAsync(async () =>
            {
                var deliveries = await clientProfile.GetWebhookDeliveries(user.StoreId, hook.Id);
                delivery = Assert.Single(deliveries);
            });

            delivery = await clientProfile.GetWebhookDelivery(user.StoreId, hook.Id, delivery.Id);
            Assert.NotNull(delivery);
            Assert.Equal(WebhookDeliveryStatus.HttpSuccess, delivery.Status);

            var newDeliveryId = await clientProfile.RedeliverWebhook(user.StoreId, hook.Id, delivery.Id);
            req = await fakeServer.GetNextRequest();
            req.Response.StatusCode = 404;
            Assert.StartsWith("BTCPayServer", Assert.Single(req.Request.Headers.UserAgent));
            await TestUtils.EventuallyAsync(async () =>
            {
                // Releasing semaphore several times may help making this test less flaky
                fakeServer.Done();
                var newDelivery = await clientProfile.GetWebhookDelivery(user.StoreId, hook.Id, newDeliveryId);
                Assert.NotNull(newDelivery);
                Assert.Equal(404, newDelivery.HttpCode);
                var req2 = await clientProfile.GetWebhookDeliveryRequest(user.StoreId, hook.Id, newDeliveryId);
                Assert.Equal(delivery.Id, req2.OriginalDeliveryId);
                Assert.True(req2.IsRedelivery);
                Assert.Equal(WebhookDeliveryStatus.HttpError, newDelivery.Status);
            });
            var deliveries = await clientProfile.GetWebhookDeliveries(user.StoreId, hook.Id);
            Assert.Equal(2, deliveries.Length);
            Assert.Equal(newDeliveryId, deliveries[0].Id);
            var jObj = await clientProfile.GetWebhookDeliveryRequest(user.StoreId, hook.Id, newDeliveryId);
            Assert.NotNull(jObj);

            TestLogs.LogInformation("Should not be able to access webhook without proper auth");
            var unauthorized = await user.CreateClient(Policies.CanCreateInvoice);
            await AssertEx.AssertHttpError(403, async () =>
            {
                await unauthorized.GetWebhookDeliveryRequest(user.StoreId, hook.Id, newDeliveryId);
            });

            TestLogs.LogInformation("Can use btcpay.store.canmodifystoresettings to query webhooks");
            clientProfile = await user.CreateClient(Policies.CanModifyStoreSettings, Policies.CanCreateInvoice);
            await clientProfile.GetWebhookDeliveryRequest(user.StoreId, hook.Id, newDeliveryId);


            TestLogs.LogInformation("Can prune deliveries");
            var cleanup = tester.PayTester.GetService<CleanupWebhookDeliveriesTask>();
            cleanup.BatchSize = 1;
            cleanup.PruneAfter = TimeSpan.Zero;
            await cleanup.Do(CancellationToken.None);
            await AssertEx.AssertHttpError(409, () => clientProfile.RedeliverWebhook(user.StoreId, hook.Id, delivery.Id));

            TestLogs.LogInformation("Testing corner cases");
            Assert.Null(await clientProfile.GetWebhookDeliveryRequest(user.StoreId, "lol", newDeliveryId));
            Assert.Null(await clientProfile.GetWebhookDeliveryRequest(user.StoreId, hook.Id, "lol"));
            Assert.Null(await clientProfile.GetWebhookDeliveryRequest(user.StoreId, "lol", "lol"));
            Assert.Null(await clientProfile.GetWebhook(user.StoreId, "lol"));
            await AssertEx.AssertHttpError(404, async () =>
            {
                await clientProfile.UpdateWebhook(user.StoreId, "lol", new UpdateStoreWebhookRequest() { Url = hook.Url });
            });

            Assert.True(await clientProfile.DeleteWebhook(user.StoreId, hook.Id));
            Assert.False(await clientProfile.DeleteWebhook(user.StoreId, hook.Id));
        }
}
