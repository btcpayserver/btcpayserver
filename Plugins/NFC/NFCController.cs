using System;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
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
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.NFC
{
    [Route("plugins/NFC")]
    public class NFCController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly InvoiceActivator _invoiceActivator;
        private readonly StoreRepository _storeRepository;

        public NFCController(IHttpClientFactory httpClientFactory,
            InvoiceRepository invoiceRepository,
            InvoiceActivator invoiceActivator,
            StoreRepository storeRepository)
        {
            _httpClientFactory = httpClientFactory;
            _invoiceRepository = invoiceRepository;
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
            if (invoice?.Status is not InvoiceStatus.New)
            {
                return NotFound();
            }

            var methods = invoice.GetPaymentPrompts();
            PaymentPrompt lnPaymentMethod = null;
            if (!methods.TryGetValue(PaymentTypes.LNURL.GetPaymentMethodId("BTC"), out var lnurlPaymentMethod) &&
                !methods.TryGetValue(PaymentTypes.LN.GetPaymentMethodId("BTC"), out lnPaymentMethod))
            {
                return BadRequest("Destination for LNURL-Withdraw was not specified");
            }

            Uri uri;
            string tag;
            try
            {
                uri = LNURL.LNURL.Parse(request.Lnurl, out tag);
                if (uri is null)
                {
                    return BadRequest("LNURL was malformed");
                }
            }
            catch (Exception e)
            {
                return BadRequest(e.Message);
            }

            if (!string.IsNullOrEmpty(tag) && !tag.Equals("withdrawRequest"))
            {
                return BadRequest("LNURL was not LNURL-Withdraw");
            }

            LNURLWithdrawRequest info;
            var httpClient = CreateHttpClient(uri);
            try
            {
                info = await LNURL.LNURL.FetchInformation(uri, tag, httpClient) as LNURLWithdrawRequest;
            }
            catch (Exception ex)
            {
                var details = ex.InnerException?.Message ?? ex.Message;
                return BadRequest($"Could not fetch info from LNURL-Withdraw: {details}");
            }

            if (info?.Callback is null)
            {
                return BadRequest("Could not fetch info from LNURL-Withdraw");
            }

            string bolt11 = null;
            if (lnPaymentMethod is not null)
            {
                if (!lnPaymentMethod.Activated)
                {
                    await _invoiceActivator.ActivateInvoicePaymentMethod(invoice.Id, lnPaymentMethod.PaymentMethodId);
                }
                LightMoney due;
                if (invoice.Type == InvoiceType.TopUp && request.Amount is not null)
                {
                    due = new LightMoney(request.Amount.Value, LightMoneyUnit.Satoshi);
                }
                else if (invoice.Type == InvoiceType.TopUp)
                {
                    return BadRequest("This is a top-up invoice and you need to provide the amount in sats to pay.");
                }
                else
                {
                    due = LightMoney.Coins(lnPaymentMethod.Calculate().Due);
                }

                if (info.MinWithdrawable > due || due > info.MaxWithdrawable)
                {
                    return BadRequest("Invoice amount is not payable with the LNURL allowed amounts.");
                }

                if (lnPaymentMethod.Activated)
                {
                    bolt11 = lnPaymentMethod.Destination;
                }
            }

            if (lnurlPaymentMethod is not null)
            {
                decimal due;
                if (invoice.Type == InvoiceType.TopUp && request.Amount is not null)
                {
                    due = new Money(request.Amount.Value, MoneyUnit.Satoshi).ToDecimal(MoneyUnit.BTC);
                }
                else if (invoice.Type == InvoiceType.TopUp)
                {
                    return BadRequest("This is a top-up invoice and you need to provide the amount in sats to pay.");
                }
                else
                {
                    due = lnurlPaymentMethod.Calculate().Due;
                }

                try
                {
                    httpClient = CreateHttpClient(info.Callback);
                    var amount = LightMoney.Coins(due);
                    var actionPath = Url.Action(nameof(UILNURLController.GetLNURLForInvoice), "UILNURL",
                        new { invoiceId = request.InvoiceId, cryptoCode = "BTC", amount = amount.MilliSatoshi });
                    var url = Request.GetAbsoluteUri(actionPath);
                    var resp = await httpClient.GetAsync(url);
                    var response = await resp.Content.ReadAsStringAsync();

                    if (resp.IsSuccessStatusCode)
                    {
                        var res = JObject.Parse(response).ToObject<LNURLPayRequest.LNURLPayRequestCallbackResponse>();
                        bolt11 = res.Pr;
                    }
                    else
                    {
                        var res = JObject.Parse(response).ToObject<LNUrlStatusResponse>();
                        return BadRequest($"Could not fetch BOLT11 invoice to pay to: {res.Reason}");

                    }
                }
                catch (Exception ex)
                {
                    return BadRequest($"Could not fetch BOLT11 invoice to pay to: {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(bolt11))
            {
                return BadRequest("Could not fetch BOLT11 invoice to pay to.");
            }

            try
            {
                var result = await info.SendRequest(bolt11, httpClient, null, null);
                if (!string.IsNullOrEmpty(result.Status) && result.Status.Equals("ok", StringComparison.InvariantCultureIgnoreCase))
                {
                    return Ok(result.Reason);
                }

                return BadRequest(result.Reason ?? "Unknown error");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        private HttpClient CreateHttpClient(Uri uri)
        {
            return _httpClientFactory.CreateClient(uri.IsOnion()
                ? LightningLikePayoutHandler.LightningLikePayoutHandlerOnionNamedClient
                : LightningLikePayoutHandler.LightningLikePayoutHandlerClearnetNamedClient);
        }
    }
}
