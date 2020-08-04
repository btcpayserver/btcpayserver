using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Models.PaymentRequestViewModels;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NBitcoin;
using NBitpayClient;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class PaymentRequestTests
    {
        public PaymentRequestTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async Task CanCreateViewUpdateAndDeletePaymentRequest()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var user2 = tester.NewAccount();
                user2.GrantAccess();

                var paymentRequestController = user.GetController<PaymentRequestController>();
                var guestpaymentRequestController = user2.GetController<PaymentRequestController>();

                var request = new UpdatePaymentRequestViewModel()
                {
                    Title = "original juice",
                    Currency = "BTC",
                    Amount = 1,
                    StoreId = user.StoreId,
                    Description = "description"
                };
                var id = (Assert
                    .IsType<RedirectToActionResult>(await paymentRequestController.EditPaymentRequest(null, request))
                    .RouteValues.Values.First().ToString());


                //permission guard for guests editing 
                Assert
                    .IsType<NotFoundResult>(await guestpaymentRequestController.EditPaymentRequest(id));

                request.Title = "update";
                Assert.IsType<RedirectToActionResult>(await paymentRequestController.EditPaymentRequest(id, request));

                Assert.Equal(request.Title,
                    Assert.IsType<ViewPaymentRequestViewModel>(Assert
                        .IsType<ViewResult>(await paymentRequestController.ViewPaymentRequest(id)).Model).Title);

                Assert.False(string.IsNullOrEmpty(id));

                Assert.IsType<ViewPaymentRequestViewModel>(Assert
                    .IsType<ViewResult>(await paymentRequestController.ViewPaymentRequest(id)).Model);

                //Archive

                Assert
                    .IsType<RedirectToActionResult>(await paymentRequestController.TogglePaymentRequestArchival(id));
                Assert.True(Assert
                    .IsType<ViewPaymentRequestViewModel>(Assert
                        .IsType<ViewResult>(await paymentRequestController.ViewPaymentRequest(id)).Model).Archived);

                Assert.Empty(Assert
                    .IsType<ListPaymentRequestsViewModel>(Assert
                        .IsType<ViewResult>(await paymentRequestController.GetPaymentRequests()).Model).Items);
                //unarchive
                Assert
                    .IsType<RedirectToActionResult>(await paymentRequestController.TogglePaymentRequestArchival(id));

                Assert.False(Assert
                    .IsType<ViewPaymentRequestViewModel>(Assert
                        .IsType<ViewResult>(await paymentRequestController.ViewPaymentRequest(id)).Model).Archived);

                Assert.Single(Assert
                    .IsType<ListPaymentRequestsViewModel>(Assert
                        .IsType<ViewResult>(await paymentRequestController.GetPaymentRequests()).Model).Items);
            }
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        public async Task CanPayPaymentRequestWhenPossible()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var paymentRequestController = user.GetController<PaymentRequestController>();

                Assert.IsType<NotFoundResult>(
                    await paymentRequestController.PayPaymentRequest(Guid.NewGuid().ToString()));


                var request = new UpdatePaymentRequestViewModel()
                {
                    Title = "original juice",
                    Currency = "BTC",
                    Amount = 1,
                    StoreId = user.StoreId,
                    Description = "description"
                };
                var response = Assert
                    .IsType<RedirectToActionResult>(paymentRequestController.EditPaymentRequest(null, request).Result)
                    .RouteValues.First();

                var invoiceId = Assert
                    .IsType<OkObjectResult>(
                        await paymentRequestController.PayPaymentRequest(response.Value.ToString(), false)).Value
                    .ToString();

                var actionResult = Assert
                    .IsType<RedirectToActionResult>(
                        await paymentRequestController.PayPaymentRequest(response.Value.ToString()));

                Assert.Equal("Checkout", actionResult.ActionName);
                Assert.Equal("Invoice", actionResult.ControllerName);
                Assert.Contains(actionResult.RouteValues,
                    pair => pair.Key == "Id" && pair.Value.ToString() == invoiceId);

                var invoice = user.BitPay.GetInvoice(invoiceId, Facade.Merchant);
                Assert.Equal(1, invoice.Price);

                request = new UpdatePaymentRequestViewModel()
                {
                    Title = "original juice with expiry",
                    Currency = "BTC",
                    Amount = 1,
                    ExpiryDate = DateTime.Today.Subtract(TimeSpan.FromDays(2)),
                    StoreId = user.StoreId,
                    Description = "description"
                };

                response = Assert
                    .IsType<RedirectToActionResult>(paymentRequestController.EditPaymentRequest(null, request).Result)
                    .RouteValues.First();

                Assert
                    .IsType<BadRequestObjectResult>(
                        await paymentRequestController.PayPaymentRequest(response.Value.ToString(), false));
            }
        }

        [Fact(Timeout = 60 * 2 * 1000)]
        [Trait("Integration", "Integration")]
        public async Task CanCancelPaymentWhenPossible()
        {
            using (var tester = ServerTester.Create())
            {
                await tester.StartAsync();
                var user = tester.NewAccount();
                user.GrantAccess();
                user.RegisterDerivationScheme("BTC");

                var paymentRequestController = user.GetController<PaymentRequestController>();

                Assert.IsType<NotFoundResult>(await
                    paymentRequestController.CancelUnpaidPendingInvoice(Guid.NewGuid().ToString(), false));

                var request = new UpdatePaymentRequestViewModel()
                {
                    Title = "original juice",
                    Currency = "BTC",
                    Amount = 1,
                    StoreId = user.StoreId,
                    Description = "description"
                };
                var response = Assert
                    .IsType<RedirectToActionResult>(paymentRequestController.EditPaymentRequest(null, request).Result)
                    .RouteValues.First();

                var paymentRequestId = response.Value.ToString();

                var invoiceId = Assert
                    .IsType<OkObjectResult>(await paymentRequestController.PayPaymentRequest(paymentRequestId, false))
                    .Value
                    .ToString();

                var actionResult = Assert
                    .IsType<RedirectToActionResult>(
                        await paymentRequestController.PayPaymentRequest(response.Value.ToString()));

                Assert.Equal("Checkout", actionResult.ActionName);
                Assert.Equal("Invoice", actionResult.ControllerName);
                Assert.Contains(actionResult.RouteValues,
                    pair => pair.Key == "Id" && pair.Value.ToString() == invoiceId);

                var invoice = user.BitPay.GetInvoice(invoiceId, Facade.Merchant);
                Assert.Equal(InvoiceState.ToString(InvoiceStatus.New), invoice.Status);
                Assert.IsType<OkObjectResult>(await
                    paymentRequestController.CancelUnpaidPendingInvoice(paymentRequestId, false));

                invoice = user.BitPay.GetInvoice(invoiceId, Facade.Merchant);
                Assert.Equal(InvoiceState.ToString(InvoiceStatus.Invalid), invoice.Status);

                Assert.IsType<BadRequestObjectResult>(await
                    paymentRequestController.CancelUnpaidPendingInvoice(paymentRequestId, false));

                invoiceId = Assert
                    .IsType<OkObjectResult>(await paymentRequestController.PayPaymentRequest(paymentRequestId, false))
                    .Value
                    .ToString();

                invoice = user.BitPay.GetInvoice(invoiceId, Facade.Merchant);

                //a hack to generate invoices for the payment request is to manually create an invocie with an order id that matches:
                user.BitPay.CreateInvoice(new Invoice(1, "USD")
                {
                    OrderId = PaymentRequestRepository.GetOrderIdForPaymentRequest(paymentRequestId)
                });
                //shouldnt crash
                await paymentRequestController.ViewPaymentRequest(paymentRequestId);
                await paymentRequestController.CancelUnpaidPendingInvoice(paymentRequestId);
            }
        }
    }
}
