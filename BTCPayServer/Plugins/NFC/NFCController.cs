using System;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly IMemoryCache _memoryCache;

        public NFCController(IHttpClientFactory httpClientFactory,
            InvoiceRepository invoiceRepository,
            InvoiceActivator invoiceActivator,
            StoreRepository storeRepository,
            IMemoryCache memoryCache)
        {
            _httpClientFactory = httpClientFactory;
            _invoiceRepository = invoiceRepository;
            _invoiceActivator = invoiceActivator;
            _storeRepository = storeRepository;
            _memoryCache = memoryCache;
        }

        public class SubmitRequest
        {
            public string Lnurl { get; set; }
            public string InvoiceId { get; set; }
            public long? Amount { get; set; }

            // LUD-290: set on the PIN completion call.
            public string Pin { get; set; }
            public string Token { get; set; }
        }

        // Public checkout endpoint POSTed via fetch without a token, like the LNURL endpoints.
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SubmitLNURLWithdrawForInvoice([FromBody] SubmitRequest request)
        {
            var invoice = await _invoiceRepository.GetInvoice(request.InvoiceId);
            if (invoice?.Status is not InvoiceStatus.New)
            {
                return NotFound();
            }

            // LUD-290: complete a PIN-protected withdraw from the stashed session.
            if (!string.IsNullOrEmpty(request.Token))
            {
                return await CompletePinProtectedWithdraw(request, invoice);
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
            LightMoney withdrawAmount = null;
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

                withdrawAmount = due;
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
                    withdrawAmount = amount;
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

            // LUD-290: PIN required - stash the fetched request (reusing its single-use k1) and ask
            // the browser for a PIN instead of calling the callback now.
            if (info.PinLimit is not null && withdrawAmount is not null &&
                withdrawAmount.MilliSatoshi >= info.PinLimit.MilliSatoshi)
            {
                if (!IsPinTransportSecure(info.Callback))
                {
                    return BadRequest("This LNURL-Withdraw requires a PIN, but its callback does not use a secure (HTTPS) connection.");
                }

                var token = Convert.ToHexString(RandomUtils.GetBytes(32));
                _memoryCache.Set(PinSessionCacheKey(token),
                    new PinWithdrawSession(info, bolt11, invoice.Id, 3),
                    new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2) });
                return Ok(new { requiresPin = true, token, amountSats = withdrawAmount.MilliSatoshi / 1000m });
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

        private async Task<IActionResult> CompletePinProtectedWithdraw(SubmitRequest request, InvoiceEntity invoice)
        {
            if (!_memoryCache.TryGetValue(PinSessionCacheKey(request.Token), out PinWithdrawSession session) || session is null)
            {
                return BadRequest("The PIN session has expired. Please tap your card again.");
            }

            if (!string.Equals(session.InvoiceId, invoice.Id, StringComparison.Ordinal))
            {
                return BadRequest("The PIN session does not match this invoice.");
            }

            if (string.IsNullOrEmpty(request.Pin))
            {
                return BadRequest("A PIN is required to complete this withdraw.");
            }

            if (session.AttemptsLeft <= 0)
            {
                _memoryCache.Remove(PinSessionCacheKey(request.Token));
                return BadRequest("Card blocked: too many incorrect PIN attempts");
            }

            try
            {
                var httpClient = CreateHttpClient(session.Info.Callback);
                var result = await session.Info.SendRequest(session.Bolt11, httpClient, request.Pin, null);
                if (!string.IsNullOrEmpty(result.Status) && result.Status.Equals("ok", StringComparison.InvariantCultureIgnoreCase))
                {
                    _memoryCache.Remove(PinSessionCacheKey(request.Token));
                    return Ok(result.Reason);
                }

                // Keep the session so the customer can retry, until blocked or out of attempts.
                session.AttemptsLeft--;
                var reason = result.Reason ?? "Unknown error";
                if (session.AttemptsLeft <= 0 || reason.Contains("blocked", StringComparison.OrdinalIgnoreCase))
                {
                    _memoryCache.Remove(PinSessionCacheKey(request.Token));
                }

                return BadRequest(reason);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // The PIN is a plaintext query param, so only forward it over https, onion, or loopback.
        internal static bool IsPinTransportSecure(Uri callback)
        {
            return callback.Scheme == Uri.UriSchemeHttps || callback.IsOnion() || callback.IsLoopback;
        }

        private static string PinSessionCacheKey(string token) => $"NFC_LNURLW_PIN_{token}";

        private HttpClient CreateHttpClient(Uri uri)
        {
            return _httpClientFactory.CreateClient(uri.IsOnion()
                ? LightningLikePayoutHandler.LightningLikePayoutHandlerOnionNamedClient
                : LightningLikePayoutHandler.LightningLikePayoutHandlerClearnetNamedClient);
        }

        // Ephemeral session so the single-use withdrawRequest/k1 is fetched once, reused across attempts.
        private class PinWithdrawSession
        {
            public PinWithdrawSession(LNURLWithdrawRequest info, string bolt11, string invoiceId, int attemptsLeft)
            {
                Info = info;
                Bolt11 = bolt11;
                InvoiceId = invoiceId;
                AttemptsLeft = attemptsLeft;
            }

            public LNURLWithdrawRequest Info { get; }
            public string Bolt11 { get; }
            public string InvoiceId { get; }
            public int AttemptsLeft { get; set; }
        }
    }
}
