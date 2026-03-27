using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable AccessToModifiedClosure

namespace BTCPayServer.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class PaymentRequestTests(ITestOutputHelper helper) : UnitTestBase(helper)
    {
        [Fact]
        [Trait("Integration", "Integration")]
        public async Task PaymentControllerTests()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            await user.MakeAdmin();
            var client = await user.CreateClient(Policies.Unrestricted);
            var viewOnly = await user.CreateClient(Policies.CanViewPaymentRequests);

            //create payment request

            //validation errors
            await AssertEx.AssertValidationError(new[] { "Amount" }, async () =>
            {
                await client.CreatePaymentRequest(user.StoreId, new() { Title = "A" });
            });
            await AssertEx.AssertValidationError(new[] { "Amount" }, async () =>
            {
                await client.CreatePaymentRequest(user.StoreId,
                    new() { Title = "A", Currency = "BTC", Amount = 0 });
            });
            await AssertEx.AssertValidationError(new[] { "Currency" }, async () =>
            {
                await client.CreatePaymentRequest(user.StoreId,
                    new() { Title = "A", Currency = "helloinvalid", Amount = 1 });
            });
            await AssertEx.AssertHttpError(403, async () =>
            {
                await viewOnly.CreatePaymentRequest(user.StoreId,
                    new() { Title = "A", Currency = "helloinvalid", Amount = 1 });
            });
            var newPaymentRequest = await client.CreatePaymentRequest(user.StoreId,
                new() { Title = "A", Currency = "USD", Amount = 1, ReferenceId = "1234"});

            //list payment request
            var paymentRequests = (await viewOnly.GetPaymentRequests(user.StoreId)).ToArray();

            Assert.NotNull(paymentRequests);
            Assert.Single(paymentRequests);
            Assert.Equal(newPaymentRequest.Id, paymentRequests.First().Id);

            //get payment request
            var paymentRequest = await viewOnly.GetPaymentRequest(user.StoreId, newPaymentRequest.Id);
            Assert.Equal(newPaymentRequest.Title, paymentRequest.Title);
            Assert.Equal(newPaymentRequest.StoreId, user.StoreId);
            Assert.Equal(newPaymentRequest.ReferenceId, paymentRequest.ReferenceId);

            //update payment request
            var updateRequest = paymentRequest;
            updateRequest.Title = "B";
            updateRequest.ReferenceId = "EmperorNicolasGeneralRockstar";
            await AssertEx.AssertHttpError(403, async () =>
            {
                await viewOnly.UpdatePaymentRequest(user.StoreId, paymentRequest.Id, updateRequest);
            });
            await client.UpdatePaymentRequest(user.StoreId, paymentRequest.Id, updateRequest);
            paymentRequest = await client.GetPaymentRequest(user.StoreId, newPaymentRequest.Id);
            Assert.Equal(updateRequest.Title, paymentRequest.Title);
            Assert.Equal(updateRequest.ReferenceId, paymentRequest.ReferenceId);

            //archive payment request
            await AssertEx.AssertHttpError(403, async () =>
            {
                await viewOnly.ArchivePaymentRequest(user.StoreId, paymentRequest.Id);
            });

            await client.ArchivePaymentRequest(user.StoreId, paymentRequest.Id);
            Assert.DoesNotContain(paymentRequest.Id,
                (await client.GetPaymentRequests(user.StoreId)).Select(data => data.Id));
            var archivedPrId = paymentRequest.Id;
            //let's test some payment stuff with the UI
            await user.RegisterDerivationSchemeAsync("BTC");
            var paymentTestPaymentRequest = await client.CreatePaymentRequest(user.StoreId,
                new() { Amount = 0.1m, Currency = "BTC", Title = "Payment test title" });

            var invoiceId = (await user.GetController<UIPaymentRequestController>()
                    .PayPaymentRequest(paymentTestPaymentRequest.Id, false)).AssertType<OkObjectResult>().Value
                .AssertType<string>();

            async Task Pay(string invoiceId2, bool partialPayment = false)
            {
                TestLogs.LogInformation($"Paying invoice {invoiceId2}");
                var invoice = user.BitPay.GetInvoice(invoiceId2);
                await tester.WaitForEvent<InvoiceDataChangedEvent>(async () =>
                {
                    TestLogs.LogInformation($"Paying address {invoice.BitcoinAddress}");
                    await tester.ExplorerNode.SendToAddressAsync(
                        BitcoinAddress.Create(invoice.BitcoinAddress, tester.ExplorerNode.Network), invoice.BtcDue);
                });
                await TestUtils.EventuallyAsync(async () =>
                {
                    Assert.Equal(Invoice.STATUS_PAID, (await user.BitPay.GetInvoiceAsync(invoiceId2)).Status);
                    if (!partialPayment)
                        Assert.Equal(PaymentRequestStatus.Processing, (await client.GetPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id)).Status);
                });
                await tester.ExplorerNode.GenerateAsync(1);
                await TestUtils.EventuallyAsync(async () =>
                {
                    Assert.Equal(Invoice.STATUS_COMPLETE, (await user.BitPay.GetInvoiceAsync(invoiceId2)).Status);
                    if (!partialPayment)
                        Assert.Equal(PaymentRequestStatus.Completed, (await client.GetPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id)).Status);
                });
            }
            await Pay(invoiceId);

            //Same thing, but with the API
            paymentTestPaymentRequest = await client.CreatePaymentRequest(user.StoreId,
                new() { Amount = 0.1m, Currency = "BTC", Title = "Payment test title" });
            var paidPrId = paymentTestPaymentRequest.Id;
            var invoiceData = await client.PayPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new PayPaymentRequestRequest());
            await Pay(invoiceData.Id);

            // Can't update the amount once the invoice has been created
            await AssertEx.AssertValidationError(new[] { "Amount" }, () => client.UpdatePaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new()
            {
                Amount = 294m
            }));

            // Let's tests some unhappy path
            paymentTestPaymentRequest = await client.CreatePaymentRequest(user.StoreId,
                new() { Amount = 0.1m, AllowCustomPaymentAmounts = false, Currency = "BTC", Title = "Payment test title" });
            await AssertEx.AssertValidationError(new[] { "Amount" }, () => client.PayPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new PayPaymentRequestRequest() { Amount = -0.04m }));
            await AssertEx.AssertValidationError(new[] { "Amount" }, () => client.PayPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new PayPaymentRequestRequest() { Amount = 0.04m }));
            await client.UpdatePaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new()
            {
                Amount = 0.1m,
                AllowCustomPaymentAmounts = true,
                Currency = "BTC",
                Title = "Payment test title"
            });
            await AssertEx.AssertValidationError(new[] { "Amount" }, () => client.PayPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new PayPaymentRequestRequest() { Amount = -0.04m }));
            invoiceData = await client.PayPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new PayPaymentRequestRequest() { Amount = 0.04m });
            Assert.Equal(0.04m, invoiceData.Amount);
            var firstPaymentId = invoiceData.Id;
            await AssertEx.AssertApiError("archived", () => client.PayPaymentRequest(user.StoreId, archivedPrId, new PayPaymentRequestRequest()));

            await client.UpdatePaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new()
            {
                Amount = 0.1m,
                AllowCustomPaymentAmounts = true,
                Currency = "BTC",
                Title = "Payment test title",
                ExpiryDate = DateTimeOffset.UtcNow - TimeSpan.FromDays(1.0)
            });

            await AssertEx.AssertApiError("expired", () => client.PayPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new PayPaymentRequestRequest()));
            await AssertEx.AssertApiError("already-paid", () => client.PayPaymentRequest(user.StoreId, paidPrId, new PayPaymentRequestRequest()));

            await client.UpdatePaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new()
            {
                Amount = 0.1m,
                AllowCustomPaymentAmounts = true,
                Currency = "BTC",
                Title = "Payment test title",
                ExpiryDate = null
            });

            await Pay(firstPaymentId, true);
            invoiceData = await client.PayPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new PayPaymentRequestRequest());

            Assert.Equal(0.06m, invoiceData.Amount);
            Assert.Equal("BTC", invoiceData.Currency);

            var expectedInvoiceId = invoiceData.Id;
            invoiceData = await client.PayPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new PayPaymentRequestRequest() { AllowPendingInvoiceReuse = true });
            Assert.Equal(expectedInvoiceId, invoiceData.Id);

            var notExpectedInvoiceId = invoiceData.Id;
            invoiceData = await client.PayPaymentRequest(user.StoreId, paymentTestPaymentRequest.Id, new PayPaymentRequestRequest() { AllowPendingInvoiceReuse = false });
            Assert.NotEqual(notExpectedInvoiceId, invoiceData.Id);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanCreateViewUpdateAndDeletePaymentRequest()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");

            var user2 = tester.NewAccount();
            await user2.GrantAccessAsync();

            var paymentRequestController = user.GetController<UIPaymentRequestController>();
            var guestpaymentRequestController = user2.GetController<UIPaymentRequestController>();

            var request = new UpdatePaymentRequestViewModel
            {
                Title = "original juice",
                Currency = "BTC",
                Amount = 1,
                StoreId = user.StoreId,
                Description = "description",
                ReferenceId = "custom-id-1"
            };
            var id = (await paymentRequestController.EditPaymentRequest(null, request)).AssertType<RedirectToActionResult>()
                .RouteValues!.Values.Last()!.ToString();

            // Assert initial Title and ReferenceId
            var repo = tester.PayTester.GetService<PaymentRequestRepository>();
            var prData = await repo.FindPaymentRequest(id, user.UserId);
            Assert.NotNull(prData);
            Assert.Equal("original juice", prData.Title);
            Assert.Equal("custom-id-1", prData.ReferenceId);

            paymentRequestController.HttpContext.SetPaymentRequestData(new PaymentRequestData { Id = id, StoreDataId = request.StoreId });

            // Permission guard for guests editing
            (await guestpaymentRequestController.EditPaymentRequest(user.StoreId, id)).AssertType<NotFoundResult>();

            request.Title = "update";
            request.ReferenceId = "custom-id-2";
            (await paymentRequestController.EditPaymentRequest(id, request)).AssertType<RedirectToActionResult>();

            // Assert updated Title and ReferenceId
            prData = await repo.FindPaymentRequest(id, user.UserId);
            Assert.NotNull(prData);
            Assert.Equal("update", prData.Title);
            Assert.Equal("custom-id-2", prData.ReferenceId);

            Assert.Equal(request.Title,
                (await paymentRequestController.ViewPaymentRequest(id)).AssertType<ViewResult>().Model
                .AssertType<ViewPaymentRequestViewModel>().Title);

            Assert.False(string.IsNullOrEmpty(id));

            (await paymentRequestController.ViewPaymentRequest(id)).AssertType<ViewResult>().Model
                .AssertType<ViewPaymentRequestViewModel>();

            // Archive
            (await paymentRequestController.TogglePaymentRequestArchival(id)).AssertType<RedirectToActionResult>();
            Assert.True((await paymentRequestController.ViewPaymentRequest(id)).AssertType<ViewResult>().Model
                .AssertType<ViewPaymentRequestViewModel>().Archived);

            Assert.Empty((await paymentRequestController.GetPaymentRequests(user.StoreId)).AssertType<ViewResult>().Model
                .AssertType<ListPaymentRequestsViewModel>().Items);

            // Unarchive
            (await paymentRequestController.TogglePaymentRequestArchival(id)).AssertType<RedirectToActionResult>();

            Assert.False((await paymentRequestController.ViewPaymentRequest(id)).AssertType<ViewResult>().Model
                .AssertType<ViewPaymentRequestViewModel>().Archived);

            Assert.Single((await paymentRequestController.GetPaymentRequests(user.StoreId)).AssertType<ViewResult>().Model
                .AssertType<ListPaymentRequestsViewModel>().Items);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CannotCreatePaymentRequestWithDuplicateReferenceId()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");

            var paymentRequestController = user.GetController<UIPaymentRequestController>();

            // Create first payment request with ReferenceId
            var request1 = new UpdatePaymentRequestViewModel
            {
                Title = "First Payment Request",
                Currency = "BTC",
                Amount = 1,
                StoreId = user.StoreId,
                Description = "First request",
                ReferenceId = "duplicate-ref-id"
            };
            var id1 = (await paymentRequestController.EditPaymentRequest(null, request1))
                .AssertType<RedirectToActionResult>()
                .RouteValues.Values.Last().ToString();

            Assert.False(string.IsNullOrEmpty(id1));

            // Try to create second payment request with same ReferenceId - should fail
            var request2 = new UpdatePaymentRequestViewModel
            {
                Title = "Second Payment Request",
                Currency = "BTC",
                Amount = 2,
                StoreId = user.StoreId,
                Description = "Second request",
                ReferenceId = "duplicate-ref-id"
            };
            var result = await paymentRequestController.EditPaymentRequest(null, request2);
            var viewResult = result.AssertType<ViewResult>();
            Assert.False(paymentRequestController.ModelState.IsValid);
            Assert.True(paymentRequestController.ModelState.ContainsKey(nameof(request2.ReferenceId)));
            Assert.Contains("already exists", paymentRequestController.ModelState[nameof(request2.ReferenceId)].Errors[0].ErrorMessage);

            // Try to edit first payment request to use a different ReferenceId - should succeed
            paymentRequestController.ModelState.Clear();
            paymentRequestController.HttpContext.SetPaymentRequestData(new PaymentRequestData { Id = id1, StoreDataId = request1.StoreId });
            request1.ReferenceId = "new-unique-ref-id";
            (await paymentRequestController.EditPaymentRequest(id1, request1)).AssertType<RedirectToActionResult>();

            // Now create second payment request with the old ReferenceId - should succeed
            paymentRequestController.HttpContext.SetPaymentRequestData(null); // Clear for new request
            var id2 = (await paymentRequestController.EditPaymentRequest(null, request2))
                .AssertType<RedirectToActionResult>()
                .RouteValues.Values.Last().ToString();
            Assert.False(string.IsNullOrEmpty(id2));

            // Try to edit second payment request to use first payment request's current ReferenceId - should fail
            paymentRequestController.ModelState.Clear();
            paymentRequestController.HttpContext.SetPaymentRequestData(new PaymentRequestData { Id = id2, StoreDataId = request2.StoreId });
            request2.ReferenceId = "new-unique-ref-id";
            result = await paymentRequestController.EditPaymentRequest(id2, request2);
            viewResult = result.AssertType<ViewResult>();
            Assert.False(paymentRequestController.ModelState.IsValid);
            Assert.True(paymentRequestController.ModelState.ContainsKey(nameof(request2.ReferenceId)));
            Assert.Contains("already exists", paymentRequestController.ModelState[nameof(request2.ReferenceId)].Errors[0].ErrorMessage);
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        public async Task CanPayPaymentRequestWhenPossible()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");

            var paymentRequestController = user.GetController<UIPaymentRequestController>();
            var repo = tester.PayTester.GetService<PaymentRequestRepository>();
            (await paymentRequestController.PayPaymentRequest(Guid.NewGuid().ToString())).AssertType<NotFoundResult>();


            var request = new UpdatePaymentRequestViewModel()
            {
                Title = "original juice",
                Currency = "BTC",
                Amount = 1,
                StoreId = user.StoreId,
                Description = "description",
                ExpiryDate = (DateTimeOffset.UtcNow + TimeSpan.FromDays(1.0)).UtcDateTime
            };
            var prId = paymentRequestController.EditPaymentRequest(null, request).Result
                .AssertType<RedirectToActionResult>()
                .RouteValues.Last().Value.ToString();

            var invoiceId = (await paymentRequestController.PayPaymentRequest(prId, false)).AssertType<OkObjectResult>().Value
                .ToString();

            var actionResult = (await paymentRequestController.PayPaymentRequest(prId))
                .AssertType<RedirectToActionResult>();

            Assert.Equal("Checkout", actionResult.ActionName);
            Assert.Equal("UIInvoice", actionResult.ControllerName);
            Assert.Contains(actionResult.RouteValues,
                pair => pair.Key == "Id" && pair.Value.ToString() == invoiceId);

            var invoice = user.BitPay.GetInvoice(invoiceId, Facade.Merchant);
            Assert.Equal(1, invoice.Price);

            // Check if we can modify a PaymentRequest after an invoice has been made
            request.ExpiryDate = null;
            var paymentRequest = await repo.FindPaymentRequest(prId, null);
            paymentRequestController.HttpContext.SetPaymentRequestData(paymentRequest);
            Assert.Equal(prId, paymentRequestController.EditPaymentRequest(prId, request).Result
                .AssertType<RedirectToActionResult>()
                .RouteValues!.Last().Value.ToString());
            paymentRequestController.HttpContext.SetPaymentRequestData(null);
            request = new UpdatePaymentRequestViewModel()
            {
                Title = "original juice with expiry",
                Currency = "BTC",
                Amount = 1,
                ExpiryDate = DateTime.Today.Subtract(TimeSpan.FromDays(2)),
                StoreId = user.StoreId,
                Description = "description"
            };

            await tester.WaitForEvent<PaymentRequestEvent>(async () =>
            {
                prId = (await paymentRequestController.EditPaymentRequest(null, request))
                    .AssertType<RedirectToActionResult>()
                    .RouteValues!.Last().Value.ToString();
            }, ev => ev.Data.Status == PaymentRequestStatus.Expired);

            (await paymentRequestController.PayPaymentRequest(prId, false)).AssertType<BadRequestObjectResult>();
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        public async Task CanCancelPaymentWhenPossible()
        {
            using var tester = CreateServerTester();
            await tester.StartAsync();
            var user = tester.NewAccount();
            await user.GrantAccessAsync();
            user.RegisterDerivationScheme("BTC");

            var paymentRequestController = user.GetController<UIPaymentRequestController>();

            (await paymentRequestController.CancelUnpaidPendingInvoice(Guid.NewGuid().ToString(), false))
                .AssertType<NotFoundResult>();

            var request = new UpdatePaymentRequestViewModel
            {
                Title = "original juice",
                Currency = "BTC",
                Amount = 1,
                StoreId = user.StoreId,
                Description = "description"
            };
            var response = paymentRequestController.EditPaymentRequest(null, request).Result
                .AssertType<RedirectToActionResult>()
                .RouteValues.Last();
            var invoiceId = response.Value.ToString();
            await paymentRequestController.PayPaymentRequest(invoiceId, false);
            (await paymentRequestController.CancelUnpaidPendingInvoice(invoiceId, false))
                .AssertType<BadRequestObjectResult>();

            request.AllowCustomPaymentAmounts = true;

            response = paymentRequestController.EditPaymentRequest(null, request).Result
                .AssertType<RedirectToActionResult>()
                .RouteValues.Last();

            var paymentRequestId = response.Value.ToString();

            invoiceId = (await paymentRequestController.PayPaymentRequest(paymentRequestId, false))
                .AssertType<OkObjectResult>()
                .Value
                .ToString();

            var actionResult = (await paymentRequestController.PayPaymentRequest(response.Value.ToString()))
                .AssertType<RedirectToActionResult>();

            Assert.Equal("Checkout", actionResult.ActionName);
            Assert.Equal("UIInvoice", actionResult.ControllerName);
            Assert.Contains(actionResult.RouteValues,
                pair => pair.Key == "Id" && pair.Value.ToString() == invoiceId);

            var invoice = user.BitPay.GetInvoice(invoiceId, Facade.Merchant);
            Assert.Equal("new", invoice.Status);
            (await paymentRequestController.CancelUnpaidPendingInvoice(paymentRequestId, false))
                .AssertType<OkObjectResult>();

            invoice = user.BitPay.GetInvoice(invoiceId, Facade.Merchant);
            Assert.Equal("invalid", invoice.Status);

            (await paymentRequestController.CancelUnpaidPendingInvoice(paymentRequestId, false))
                .AssertType<BadRequestObjectResult>();

            invoiceId = (await paymentRequestController.PayPaymentRequest(paymentRequestId, false))
                .AssertType<OkObjectResult>()
                .Value
                .ToString();

            await user.BitPay.GetInvoiceAsync(invoiceId, Facade.Merchant);

            //a hack to generate invoices for the payment request is to manually create an invoice with an order id that matches:
            user.BitPay.CreateInvoice(new Invoice(1, "USD")
            {
                OrderId = PaymentRequestRepository.GetOrderIdForPaymentRequest(paymentRequestId)
            });
            //shouldn't crash
            await paymentRequestController.ViewPaymentRequest(paymentRequestId);
            await paymentRequestController.CancelUnpaidPendingInvoice(paymentRequestId);
        }
    }
}
