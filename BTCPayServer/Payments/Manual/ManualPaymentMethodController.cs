using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Payments.Bitcoin
{
    [Route("[controller]")]
    [XFrameOptionsAttribute(null)]
    [ReferrerPolicyAttribute("origin")]
    [AllowAnonymous]
    public class ManualPaymentMethodController : Controller
    {
        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly StoreRepository _StoreRepository;
        private readonly InvoiceRepository _InvoiceRepository;
        private readonly BTCPayNetworkProvider _BtcPayNetworkProvider;
        private readonly EventAggregator _EventAggregator;
        private readonly SignInManager<ApplicationUser> _SignInManager;

        public ManualPaymentMethodController(UserManager<ApplicationUser> userManager, StoreRepository storeRepository,
            InvoiceRepository invoiceRepository, BTCPayNetworkProvider btcPayNetworkProvider,
            EventAggregator eventAggregator, SignInManager<ApplicationUser> signInManager)
        {
            _UserManager = userManager;
            _StoreRepository = storeRepository;
            _InvoiceRepository = invoiceRepository;
            _BtcPayNetworkProvider = btcPayNetworkProvider;
            _EventAggregator = eventAggregator;
            _SignInManager = signInManager;
        }

        [HttpPost("add-payment")]
        public async Task<IActionResult> AddPayment(AddPaymentRequest model)
        {
            var invoice = await _InvoiceRepository.GetInvoice(model.InvoiceId, false);
            var manualPayment = invoice.GetSupportedPaymentMethod<ManualPaymentSettings>().FirstOrDefault();
            if (manualPayment == null)
            {
                return NotFound();
            }


            if (ModelState.IsValid)
            {
                if (!manualPayment.AllowCustomerToMarkPaid &&
                    (!_SignInManager.IsSignedIn(User) ||
                     !await _StoreRepository.HasAccessToStore(invoice.StoreId, _UserManager.GetUserId(User))))
                {
                    return Forbid();
                }

                var network = _BtcPayNetworkProvider.GetNetwork<StubBTCPayNetwork>(string.Empty);
                var currentDue = invoice.GetPaymentMethod(network, ManualPaymentType.Instance).Calculate().Due;
                var payment = await _InvoiceRepository.AddPayment(model.InvoiceId, DateTimeOffset.Now,
                    new ManualPaymentData()
                    {
                        Network = network,
                        CurrencyCode = invoice.ProductInformation.Currency,
                        Confirmed = manualPayment.SetPaymentAsConfirmed,
                        Amount = manualPayment.AllowPartialPaymentInput && model.PartialAmount.HasValue
                            ? model.PartialAmount.Value
                            : currentDue.ToDecimal(MoneyUnit.BTC),
                        Id = Guid.NewGuid().ToString(),
                        Notes = manualPayment.AllowPaymentNote ? model.Note : string.Empty
                    }, network, true);


                _EventAggregator.Publish(
                    new InvoiceEvent(invoice, 1002, InvoiceEvent.ReceivedPayment) {Payment = payment});

                if (model.RedirectToInvoice)
                {
                    return RedirectToAction("Checkout", "Invoice", new {InvoiceId = invoice.Id});
                }

                return Ok();
            }

            return ValidationProblem();
        }

        [HttpPost("confirm-payment")]
        public async Task<IActionResult> ConfirmPayment(ConfirmPaymentRequest model)
        {

            var matchedPaymentIdInvoices = await _InvoiceRepository.GetInvoices(new InvoiceQuery() {TextSearch = model.PaymentId});
            var invoice = matchedPaymentIdInvoices.FirstOrDefault();
            if (invoice == null)
            {
                return NotFound();
            }
            var manualPayment = invoice.GetSupportedPaymentMethod<ManualPaymentSettings>().FirstOrDefault();
            if (manualPayment == null)
            {
                return NotFound();
            }

            var manualPayments = invoice.GetPayments()
                .Where(entity => entity.GetPaymentMethodId().PaymentType == ManualPaymentType.Instance);

            if (ModelState.IsValid)
            {
                if (!_SignInManager.IsSignedIn(User) ||
                    !await _StoreRepository.HasAccessToStore(invoice.StoreId, _UserManager.GetUserId(User)))
                {
                    return Forbid();
                }

                foreach (var paymentEntity in manualPayments)
                {
                    var manualPaymentData = paymentEntity.GetCryptoPaymentData() as ManualPaymentData;
                    if (manualPaymentData.Id == model.PaymentId)
                    {
                        manualPaymentData.Confirmed = true;
                        paymentEntity.SetCryptoPaymentData(manualPaymentData);
                        await _InvoiceRepository.UpdatePayments(new List<PaymentEntity>() {paymentEntity});
                        if (string.IsNullOrEmpty(model.RedirectUrl))
                        {
                            return Ok();
                        }

                        return Redirect(model.RedirectUrl);
                    }
                }
            }

            return ValidationProblem();
        }
    }

    public class AddPaymentRequest
    {
        public string Note { get; set; }
        public decimal? PartialAmount { get; set; }
        [Required] public string InvoiceId { get; set; }

        public bool RedirectToInvoice { get; set; } = false;
    }

    public class ConfirmPaymentRequest
    {
        public string PaymentId { get; set; }
        public string RedirectUrl { get; set; }
    }
}
