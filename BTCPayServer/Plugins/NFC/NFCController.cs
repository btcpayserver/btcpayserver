using System;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Plugins.NFC
{
    [Route("plugins/NFC")]
    public class NFCController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly UILNURLController _uilnurlController;
        private readonly InvoiceActivator _invoiceActivator;
        private readonly StoreRepository _storeRepository;

        public NFCController(IHttpClientFactory httpClientFactory,
            InvoiceRepository invoiceRepository,
            UILNURLController uilnurlController,
            InvoiceActivator invoiceActivator,
            StoreRepository storeRepository)
        {
            _httpClientFactory = httpClientFactory;
            _invoiceRepository = invoiceRepository;
            _uilnurlController = uilnurlController;
            _invoiceActivator = invoiceActivator;
            _storeRepository = storeRepository;
        }

        public class SubmitRequest
        {
            public string Lnurl { get; set; }
            public string InvoiceId { get; set; }
            public long? Amount { get; set; }
        }

        [AllowAnonymous]
        public async Task<IActionResult> SubmitLNURLWithdrawForInvoice([FromBody] SubmitRequest request)
        {
            var invoice = await _invoiceRepository.GetInvoice(request.InvoiceId);
            if (invoice?.Status is not InvoiceStatusLegacy.New)
            {
                return NotFound();
            }

            var methods = invoice.GetPaymentMethods();
            PaymentMethod lnPaymentMethod = null;
            if (!methods.TryGetValue(new PaymentMethodId("BTC", PaymentTypes.LNURLPay), out var lnurlPaymentMethod) &&
                !methods.TryGetValue(new PaymentMethodId("BTC", PaymentTypes.LightningLike), out lnPaymentMethod))
            {
                return BadRequest("destination for lnurlw was not specified");
            }

            Uri uri;
            string tag;
            try
            {
                uri = LNURL.LNURL.Parse(request.Lnurl, out tag);
                if (uri is null)
                {
                    return BadRequest("lnurl was malformed");
                }
            }
            catch (Exception e)
            {
                return BadRequest(e.Message);
            }


            if (!string.IsNullOrEmpty(tag) && !tag.Equals("withdrawRequest"))
            {
                return BadRequest("lnurl was not lnurl-withdraw");
            }

            var httpClient = _httpClientFactory.CreateClient(uri.IsOnion()
                ? LightningLikePayoutHandler.LightningLikePayoutHandlerOnionNamedClient
                : LightningLikePayoutHandler.LightningLikePayoutHandlerClearnetNamedClient);
            var info = (await
                LNURL.LNURL.FetchInformation(uri, "withdrawRequest", httpClient)) as LNURLWithdrawRequest;
            if (info?.Callback is null)
            {
                return BadRequest("Could not fetch info from lnurl-withdraw ");
            }

            httpClient = _httpClientFactory.CreateClient(info.Callback.IsOnion()
                ? LightningLikePayoutHandler.LightningLikePayoutHandlerOnionNamedClient
                : LightningLikePayoutHandler.LightningLikePayoutHandlerClearnetNamedClient);

            string bolt11 = null;

            if (lnPaymentMethod is not null)
            {
                if (lnPaymentMethod.GetPaymentMethodDetails() is LightningLikePaymentMethodDetails
                    {
                        Activated: false
                    } lnPMD)
                {
                    var store = await _storeRepository.FindStore(invoice.StoreId);
                    await _invoiceActivator.ActivateInvoicePaymentMethod(lnPaymentMethod.GetId(), invoice, store);
                }

                lnPMD = lnPaymentMethod.GetPaymentMethodDetails() as LightningLikePaymentMethodDetails;
                LightMoney due;
                if (invoice.Type == InvoiceType.TopUp && request.Amount is not null)
                {
                    due = new LightMoney(request.Amount.Value, LightMoneyUnit.Satoshi);
                }else if (invoice.Type == InvoiceType.TopUp)
                {
                    return BadRequest("This is a topup invoice and you need to provide the amount in sats to pay.");
                }
                else
                {
                    due =  new LightMoney(lnPaymentMethod.Calculate().Due);
                }
                if (info.MinWithdrawable > due || due > info.MaxWithdrawable)
                {
                    return BadRequest("invoice amount is not payable with the lnurl allowed amounts.");
                }

                if (lnPMD?.Activated is true)
                {
                    bolt11 = lnPMD.BOLT11;
                }
            }

            if (lnurlPaymentMethod is not null)
            {
                Money due;
                if (invoice.Type == InvoiceType.TopUp && request.Amount is not null)
                {
                    due = new Money(request.Amount.Value, MoneyUnit.Satoshi);
                }else if (invoice.Type == InvoiceType.TopUp)
                {
                    return BadRequest("This is a topup invoice and you need to provide the amount in sats to pay.");
                }
                else
                {
                    due =  lnurlPaymentMethod.Calculate().Due;
                }

                var response = await _uilnurlController.GetLNURLForInvoice(request.InvoiceId, "BTC",
                    due.Satoshi);

                if (response is ObjectResult objectResult)
                {
                    switch (objectResult.Value)
                    {
                        case LNURLPayRequest.LNURLPayRequestCallbackResponse lnurlPayRequestCallbackResponse:
                            bolt11 = lnurlPayRequestCallbackResponse.Pr;
                            break;
                        case LNUrlStatusResponse lnUrlStatusResponse:

                            return BadRequest(
                                $"Could not fetch bolt11 invoice to pay to: {lnUrlStatusResponse.Reason}");
                    }
                }
            }

            if (bolt11 is null)
            {
                return BadRequest("Could not fetch bolt11 invoice to pay to.");
            }

            var result = await info.SendRequest(bolt11, httpClient);
            if (result.Status.Equals("ok", StringComparison.InvariantCultureIgnoreCase))
            {
                return Ok(result.Reason);
            }

            return BadRequest(result.Reason);
        }
    }
}
