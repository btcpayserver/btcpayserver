using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitpayClient;
using OpenIddict.Abstractions;
using OpenQA.Selenium;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class ManualPaymentsTests
    {
        public ManualPaymentsTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }


        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanSetAndUseManualPaymentMethod()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var controller = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);

                var vm = Assert.IsType<StoreViewModel>(Assert.IsType<ViewResult>(controller.UpdateStore()).Model);
                //disabled by default
                Assert.False(
                    vm.ThirdPartyPaymentMethods.SingleOrDefault(method => method.Provider == "Manual")?.Enabled);

                var updateVM = new UpdateManualPaymentSettings()
                {
                    Enabled = true,
                    DisplayText = "text",
                    AllowPaymentNote = true,
                    SetPaymentAsConfirmed = true,
                    AllowCustomerToMarkPaid = false,
                    AllowPartialPaymentInput = true
                };

                //check that it updates ok
                var result =
                    Assert.IsType<RedirectToActionResult>(
                        await controller.UpdateManualSettings(user.StoreId, updateVM));
                Assert.True(result.RouteValues.ContainsKey("StatusMessage"));
                Assert.Equal(StatusMessageModel.StatusSeverity.Success,
                    new StatusMessageModel(result.RouteValues["StatusMessage"].ToString()).Severity);

                controller = tester.PayTester.GetController<StoresController>(user.UserId, user.StoreId);
                vm = Assert.IsType<StoreViewModel>(Assert.IsType<ViewResult>(controller.UpdateStore()).Model);
                //enabled now
                Assert.True(
                    vm.ThirdPartyPaymentMethods.SingleOrDefault(method => method.Provider == "Manual")?.Enabled);


                var invoice = user.BitPay.CreateInvoice(
                    new Invoice()
                    {
                        Price = 5000.0m,
                        Currency = "USD",
                        PosData = "posData",
                        OrderId = "orderId",
                        ItemDesc = "Some description",
                        FullNotifications = true
                    }, Facade.Merchant);

                var paymentMethodId = ManualPaymentSettings.StaticPaymentId;
                var invoiceController = tester.PayTester.GetController<InvoiceController>(user.UserId, user.StoreId);
                var paymentModel = Assert.IsType<PaymentModel>(Assert.IsType<ViewResult>(invoiceController.Checkout(
                    invoice.Id, null,
                    paymentMethodId.ToString())));
                //Manual displays fine too
                Assert.Equal(paymentMethodId, PaymentMethodId.Parse(paymentModel.PaymentMethodId));
                Assert.NotNull(paymentModel.UISettings);

                var anonManualController = tester.PayTester.GetController<ManualPaymentMethodController>();
                var manualController =
                    tester.PayTester.GetController<ManualPaymentMethodController>(user.UserId, user.StoreId);

                var addPayment = new AddPaymentRequest()
                {
                    InvoiceId = invoice.Id, Note = "A note with stuff", PartialAmount = invoice.Price / 2
                };
                Assert.IsType<ForbidResult>(await anonManualController.AddPayment(addPayment));
                Assert.IsType<OkResult>(await manualController.AddPayment(addPayment));

                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.True(invoice.BtcPaid.ToDecimal(MoneyUnit.BTC) == invoice.Price / 2);
                });

                addPayment = new AddPaymentRequest()
                {
                    InvoiceId = invoice.Id, Note = "A note with stuff", PartialAmount = invoice.Price / 2
                };
                Assert.IsType<OkResult>(await manualController.AddPayment(addPayment));

                TestUtils.Eventually(() =>
                {
                    invoice = user.BitPay.GetInvoice(invoice.Id);
                    Assert.Equal(invoice.Status, Invoice.STATUS_COMPLETE);
                });
            }
        }

        [Trait("Selenium", "Selenium")]
        [Fact]
        public async Task CanUseManualCheckout()
        {
            using (var s = SeleniumTester.Create())
            {
                s.Start();
                s.RegisterNewUser();
                var store = s.CreateNewStore();
                s.Driver.FindElement(By.Id("Modify-Manual")).ForceClick();
                s.Driver.FindElement(By.Name("Enabled")).ForceClick();
                s.Driver.FindElement(By.Name("command")).ForceClick();

                s.CreateInvoice("d");
                s.Driver.FindElement(By.ClassName("invoice-checkout-link")).Click();
                Assert.NotEmpty(s.Driver.FindElements(By.Id("manual-method-checkout-template")));
                s.Dispose();
            }
        }
    }
}
