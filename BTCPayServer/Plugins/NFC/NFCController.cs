using System;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<NFCController> _logger;
        private readonly UILNURLController _lnurlController;

        public NFCController(IHttpClientFactory httpClientFactory,
            InvoiceRepository invoiceRepository,
            InvoiceActivator invoiceActivator,
            StoreRepository storeRepository,
            ILogger<NFCController> logger,
            UILNURLController lnurlController)
        {
            _httpClientFactory = httpClientFactory;
            _invoiceRepository = invoiceRepository;
            _invoiceActivator = invoiceActivator;
            _storeRepository = storeRepository;
            _logger = logger;
            _lnurlController = lnurlController;
        }

        public class SubmitRequest
        {
            public string Lnurl { get; set; }
            public string InvoiceId { get; set; }
            public long? Amount { get; set; }
        }

        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [HttpPost]
        public async Task<IActionResult> SubmitLNURLWithdrawForInvoice([FromBody] SubmitRequest request)
        {
            var invoice = await _invoiceRepository.GetInvoice(request.InvoiceId);
            if (invoice?.Status is not InvoiceStatus.New)
            {
                return NotFound();
            }

            var store = await _storeRepository.FindStore(invoice.StoreId);
            if (store?.GetStoreBlob().NfcEnabled is not true)
            {
                return NotFound();
            }

            LightMoney topUpAmount = null;
            if (invoice.Type == InvoiceType.TopUp)
            {
                if (request.Amount is null)
                {
                    return BadRequest("This is a top-up invoice and you need to provide the amount in sats to pay.");
                }

                if (request.Amount <= 0 || request.Amount > long.MaxValue / 1000)
                {
                    return BadRequest("The top-up amount must be a positive number of sats.");
                }

                topUpAmount = new LightMoney(request.Amount.Value, LightMoneyUnit.Satoshi);
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
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                return BadRequest("Could not fetch info from LNURL-Withdraw");
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
                if (topUpAmount is not null)
                {
                    due = topUpAmount;
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
                if (topUpAmount is not null)
                {
                    due = topUpAmount.ToDecimal(LightMoneyUnit.BTC);
                }
                else
                {
                    due = lnurlPaymentMethod.Calculate().Due;
                }

                try
                {
                    var amount = LightMoney.Coins(due);
                    if (info.MinWithdrawable > amount || amount > info.MaxWithdrawable)
                    {
                        return BadRequest("Invoice amount is not payable with the LNURL allowed amounts.");
                    }

                    _lnurlController.ControllerContext = ControllerContext;
                    var response = await _lnurlController.GetLNURLForInvoice(request.InvoiceId, "BTC", amount.MilliSatoshi);
                    if (response is OkObjectResult { Value: JObject callbackResponse })
                    {
                        bolt11 = callbackResponse.Value<string>("pr");
                    }
                    else
                    {
                        var reason = (response as ObjectResult)?.Value is LNUrlStatusResponse status
                            ? status.Reason
                            : "Unknown error";
                        return BadRequest($"Could not fetch BOLT11 invoice to pay to: {reason}");
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
                httpClient = CreateHttpClient(info.Callback);
                var result = await info.SendRequest(bolt11, httpClient, null, null);
                if (!string.IsNullOrEmpty(result.Status) && result.Status.Equals("ok", StringComparison.InvariantCultureIgnoreCase))
                {
                    return Ok(result.Reason);
                }

                return BadRequest(result.Reason ?? "Unknown error");
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                return BadRequest("Could not complete the LNURL-Withdraw request");
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
