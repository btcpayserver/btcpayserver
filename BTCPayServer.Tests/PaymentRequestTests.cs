using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class PaymentRequestTests : UnitTestBase
    {
        public PaymentRequestTests(ITestOutputHelper helper) : base(helper)
        {
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
            var id = Assert
                .IsType<RedirectToActionResult>(await paymentRequestController.EditPaymentRequest(null, request))
                .RouteValues.Values.Last().ToString();

            // Assert initial Title and ReferenceId
            var repo = tester.PayTester.GetService<PaymentRequestRepository>();
            var prData = await repo.FindPaymentRequest(id, user.UserId);
            Assert.NotNull(prData);
            Assert.Equal("original juice", prData.GetBlob().Title);
            Assert.Equal("custom-id-1", prData.ReferenceId);

            paymentRequestController.HttpContext.SetPaymentRequestData(new PaymentRequestData { Id = id, StoreDataId = request.StoreId });

            // Permission guard for guests editing 
            Assert
                .IsType<NotFoundResult>(await guestpaymentRequestController.EditPaymentRequest(user.StoreId, id));

            request.Title = "update";
            request.ReferenceId = "custom-id-2";
            Assert.IsType<RedirectToActionResult>(await paymentRequestController.EditPaymentRequest(id, request));

            // Assert updated Title and ReferenceId
            prData = await repo.FindPaymentRequest(id, user.UserId);
            Assert.NotNull(prData);
            Assert.Equal("update", prData.GetBlob().Title);
            Assert.Equal("custom-id-2", prData.ReferenceId);

            Assert.Equal(request.Title,
                Assert.IsType<ViewPaymentRequestViewModel>(Assert
                    .IsType<ViewResult>(await paymentRequestController.ViewPaymentRequest(id)).Model).Title);

            Assert.False(string.IsNullOrEmpty(id));

            Assert.IsType<ViewPaymentRequestViewModel>(Assert
                .IsType<ViewResult>(await paymentRequestController.ViewPaymentRequest(id)).Model);

            // Archive
            Assert
                .IsType<RedirectToActionResult>(await paymentRequestController.TogglePaymentRequestArchival(id));
            Assert.True(Assert
                .IsType<ViewPaymentRequestViewModel>(Assert
                    .IsType<ViewResult>(await paymentRequestController.ViewPaymentRequest(id)).Model).Archived);

            Assert.Empty(Assert
                .IsType<ListPaymentRequestsViewModel>(Assert
                    .IsType<ViewResult>(await paymentRequestController.GetPaymentRequests(user.StoreId)).Model).Items);

            // Unarchive
            Assert
                .IsType<RedirectToActionResult>(await paymentRequestController.TogglePaymentRequestArchival(id));

            Assert.False(Assert
                .IsType<ViewPaymentRequestViewModel>(Assert
                    .IsType<ViewResult>(await paymentRequestController.ViewPaymentRequest(id)).Model).Archived);

            Assert.Single(Assert
                .IsType<ListPaymentRequestsViewModel>(Assert
                    .IsType<ViewResult>(await paymentRequestController.GetPaymentRequests(user.StoreId)).Model).Items);
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
            var id1 = Assert
                .IsType<RedirectToActionResult>(await paymentRequestController.EditPaymentRequest(null, request1))
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
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(paymentRequestController.ModelState.IsValid);
            Assert.True(paymentRequestController.ModelState.ContainsKey(nameof(request2.ReferenceId)));
            Assert.Contains("already exists", paymentRequestController.ModelState[nameof(request2.ReferenceId)].Errors[0].ErrorMessage);

            // Try to edit first payment request to use a different ReferenceId - should succeed
            paymentRequestController.ModelState.Clear();
            paymentRequestController.HttpContext.SetPaymentRequestData(new PaymentRequestData { Id = id1, StoreDataId = request1.StoreId });
            request1.ReferenceId = "new-unique-ref-id";
            Assert.IsType<RedirectToActionResult>(await paymentRequestController.EditPaymentRequest(id1, request1));

            // Now create second payment request with the old ReferenceId - should succeed
            paymentRequestController.HttpContext.SetPaymentRequestData(null); // Clear for new request
            var id2 = Assert
                .IsType<RedirectToActionResult>(await paymentRequestController.EditPaymentRequest(null, request2))
                .RouteValues.Values.Last().ToString();
            Assert.False(string.IsNullOrEmpty(id2));

            // Try to edit second payment request to use first payment request's current ReferenceId - should fail
            paymentRequestController.ModelState.Clear();
            paymentRequestController.HttpContext.SetPaymentRequestData(new PaymentRequestData { Id = id2, StoreDataId = request2.StoreId });
            request2.ReferenceId = "new-unique-ref-id";
            result = await paymentRequestController.EditPaymentRequest(id2, request2);
            viewResult = Assert.IsType<ViewResult>(result);
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
            Assert.IsType<NotFoundResult>(
                await paymentRequestController.PayPaymentRequest(Guid.NewGuid().ToString()));


            var request = new UpdatePaymentRequestViewModel()
            {
                Title = "original juice",
                Currency = "BTC",
                Amount = 1,
                StoreId = user.StoreId,
                Description = "description",
                ExpiryDate = (DateTimeOffset.UtcNow + TimeSpan.FromDays(1.0)).UtcDateTime
            };
            var prId = Assert
                .IsType<RedirectToActionResult>(paymentRequestController.EditPaymentRequest(null, request).Result)
                .RouteValues.Last().Value.ToString();

            var invoiceId = Assert
                .IsType<OkObjectResult>(
                    await paymentRequestController.PayPaymentRequest(prId, false)).Value
                .ToString();

            var actionResult = Assert
                .IsType<RedirectToActionResult>(
                    await paymentRequestController.PayPaymentRequest(prId));

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
            Assert
                .IsType<RedirectToActionResult>(paymentRequestController.EditPaymentRequest(prId, request).Result)
                .RouteValues.Last().Value.ToString();
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

            prId = Assert
                .IsType<RedirectToActionResult>(paymentRequestController.EditPaymentRequest(null, request).Result)
                .RouteValues.Last().Value.ToString();

            Assert
                .IsType<BadRequestObjectResult>(
                    await paymentRequestController.PayPaymentRequest(prId, false));
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

            Assert.IsType<NotFoundResult>(await
                paymentRequestController.CancelUnpaidPendingInvoice(Guid.NewGuid().ToString(), false));

            var request = new UpdatePaymentRequestViewModel
            {
                Title = "original juice",
                Currency = "BTC",
                Amount = 1,
                StoreId = user.StoreId,
                Description = "description"
            };
            var response = Assert
                .IsType<RedirectToActionResult>(paymentRequestController.EditPaymentRequest(null, request).Result)
                .RouteValues.Last();
            var invoiceId = response.Value.ToString();
            await paymentRequestController.PayPaymentRequest(invoiceId, false);
            Assert.IsType<BadRequestObjectResult>(await
                paymentRequestController.CancelUnpaidPendingInvoice(invoiceId, false));

            request.AllowCustomPaymentAmounts = true;

            response = Assert
                .IsType<RedirectToActionResult>(paymentRequestController.EditPaymentRequest(null, request).Result)
                .RouteValues.Last();

            var paymentRequestId = response.Value.ToString();

            invoiceId = Assert
                .IsType<OkObjectResult>(await paymentRequestController.PayPaymentRequest(paymentRequestId, false))
                .Value
                .ToString();

            var actionResult = Assert
                .IsType<RedirectToActionResult>(
                    await paymentRequestController.PayPaymentRequest(response.Value.ToString()));

            Assert.Equal("Checkout", actionResult.ActionName);
            Assert.Equal("UIInvoice", actionResult.ControllerName);
            Assert.Contains(actionResult.RouteValues,
                pair => pair.Key == "Id" && pair.Value.ToString() == invoiceId);

            var invoice = user.BitPay.GetInvoice(invoiceId, Facade.Merchant);
            Assert.Equal("new", invoice.Status);
            Assert.IsType<OkObjectResult>(await
                paymentRequestController.CancelUnpaidPendingInvoice(paymentRequestId, false));

            invoice = user.BitPay.GetInvoice(invoiceId, Facade.Merchant);
            Assert.Equal("invalid", invoice.Status);

            Assert.IsType<BadRequestObjectResult>(await
                paymentRequestController.CancelUnpaidPendingInvoice(paymentRequestId, false));

            invoiceId = Assert
                .IsType<OkObjectResult>(await paymentRequestController.PayPaymentRequest(paymentRequestId, false))
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
