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
                var paymentModel = Assert.IsType<PaymentModel>(Assert.IsType<ViewResult>(await invoiceController.Checkout(
                    invoice.Id, null,
                    paymentMethodId.ToString())).Model);
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
                    Assert.Equal(Invoice.EXSTATUS_PAID_PARTIAL, invoice.ExceptionStatus);
                    Assert.Equal(1, invoice.CryptoInfo.Length);
                    Assert.Equal(1, invoice.CryptoInfo.First().TxCount);
                    Assert.Equal(invoice.Price / 2, decimal.Parse( invoice.CryptoInfo.First().CryptoPaid));
                    Assert.Equal(invoice.Price / 2, invoice.CryptoInfo.First().Payments.First().Value);
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
                UpdateManualPaymnetSettings(s,
                    new UpdateManualPaymentSettings()
                    {
                        Enabled = true,
                        DisplayText = "test text",
                        AllowCustomerToMarkPaid = false,
                        AllowPartialPaymentInput = false,
                        AllowPaymentNote = false,
                        SetPaymentAsConfirmed = true
                    });


                //let's verify that the manual checkout templates load
                s.CreateInvoice("aaa", "pavlenex@sucks.com");
                s.Driver.FindElement(By.Id("manual-method-checkout-template"));
                var manualContainer = s.Driver.FindElement(By.Id("manual-payment-container"));
                Assert.NotNull(manualContainer);

                //verify display text is shown
                var displayText = manualContainer.FindElement(By.Id("manual-display-text"));
                Assert.True(displayText.Displayed);
                Assert.Equal("test text", displayText.Text);
                // verify payment note is not shown
                Assert.Throws<NoSuchElementException>(() => manualContainer.FindElement(By.Id("manual-payment-notes")));
                // verify partial payment option is not shown
                Assert.Throws<NoSuchElementException>(() =>
                    manualContainer.FindElement(By.Id("partial-payment-input")));

                //let's mark as paid!
                var addPayment = manualContainer.FindElement(By.Id("btn-add-payment"));
                Assert.True(addPayment.Displayed);
                addPayment.Click();
                TestUtils.Eventually(() =>
                {
                    //invoice agrees that it was paid
                    Assert.True(s.Driver.FindElement(By.Id("paid")).GetAttribute("class")
                        .Contains("active", StringComparison.InvariantCultureIgnoreCase));
                });
                s.GoToHome();
                s.GoToStore(store.storeId);
                s.Driver.FindElement(By.Id("Modify-Manual")).ForceClick();
                UpdateManualPaymnetSettings(s,
                    new UpdateManualPaymentSettings()
                    {
                        Enabled = true,
                        AllowCustomerToMarkPaid = false,
                        AllowPartialPaymentInput = true,
                        AllowPaymentNote = true,
                        SetPaymentAsConfirmed = true
                    });

                s.CreateInvoice("bbb", "pavlenex@sucks.com");
                s.Driver.FindElement(By.Id("manual-method-checkout-template"));
                manualContainer = s.Driver.FindElement(By.Id("manual-payment-container"));
                // verify payment note is  shown
                Assert.True(manualContainer.FindElement(By.Id("manual-payment-notes")).Displayed);
                // verify partial payment option is  shown
                var partialInputElement = manualContainer.FindElement(By.Id("partial-payment-input"));
                Assert.True(partialInputElement.Displayed);


                //let's send a partial payment
                addPayment = manualContainer.FindElement(By.Id("btn-add-payment"));
                Assert.True(addPayment.Displayed);
                partialInputElement.SendKeys("50");
                addPayment.Click();

                TestUtils.Eventually(() =>
                {
                    Assert.False(s.Driver.FindElement(By.Id("paid")).GetAttribute("class")
                        .Contains("active", StringComparison.InvariantCultureIgnoreCase));
                });
                //invoice agrees that it was NOT fully paid
                addPayment.Click();
                TestUtils.Eventually(() =>
                {
                    //invoice agrees that it was paid
                    Assert.True(s.Driver.FindElement(By.Id("paid")).GetAttribute("class")
                        .Contains("active", StringComparison.InvariantCultureIgnoreCase));
                });

                s.Dispose();
            }
        }

        public void UpdateManualPaymnetSettings(SeleniumTester s, UpdateManualPaymentSettings settings)
        {
            s.SetCheckbox(s, nameof(settings.Enabled), settings.Enabled);
            s.SetCheckbox(s, nameof(settings.AllowPaymentNote), settings.AllowPaymentNote);
            s.SetCheckbox(s, nameof(settings.AllowPartialPaymentInput), settings.AllowPartialPaymentInput);
            s.SetCheckbox(s, nameof(settings.SetPaymentAsConfirmed), settings.SetPaymentAsConfirmed);
            s.SetCheckbox(s, nameof(settings.AllowCustomerToMarkPaid), settings.AllowCustomerToMarkPaid);
            var displayTextElement = s.Driver.FindElement(By.Name(nameof(settings.DisplayText)));
            displayTextElement.Clear();
            displayTextElement.SendKeys(settings.DisplayText);

            s.Driver.FindElement(By.Name("command")).Click();
        }
    }
}
