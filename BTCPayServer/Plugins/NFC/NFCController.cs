using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
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
        [IgnoreAntiforgeryToken]
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

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return BadRequest("LNURL scheme must be http or https");
            }

            var cancellationToken = HttpContext.RequestAborted;
            IPAddress[] uriPinned = null;
            if (!uri.IsOnion())
            {
                var (uriSafe, uriAddrs, uriReason) = await ResolveAndValidateAsync(uri, cancellationToken);
                if (!uriSafe)
                {
                    return BadRequest(uriReason);
                }
                uriPinned = uriAddrs;
            }

            if (!string.IsNullOrEmpty(tag) && !tag.Equals("withdrawRequest"))
            {
                return BadRequest("LNURL was not LNURL-Withdraw");
            }

            LNURLWithdrawRequest info;
            var httpClient = CreateSafeHttpClient(uri, uriPinned);
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

            if (info.Callback.Scheme != Uri.UriSchemeHttp && info.Callback.Scheme != Uri.UriSchemeHttps)
            {
                return BadRequest("LNURL callback scheme must be http or https");
            }

            IPAddress[] callbackPinned = null;
            if (!info.Callback.IsOnion())
            {
                var (cbSafe, cbAddrs, cbReason) = await ResolveAndValidateAsync(info.Callback, cancellationToken);
                if (!cbSafe)
                {
                    return BadRequest($"LNURL callback rejected: {cbReason}");
                }
                callbackPinned = cbAddrs;
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
                    httpClient = CreateSafeHttpClient(info.Callback, callbackPinned);
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

        // Onion hosts route through the SOCKS5-configured named client (Tor handles
        // routing; no DNS resolution done client-side). Clearnet hosts get a
        // per-request handler that pins the connect() socket to the pre-resolved
        // safe IPs, forbidding automatic redirect-following.
        private HttpClient CreateSafeHttpClient(Uri uri, IPAddress[] pinnedAddresses)
        {
            if (uri.IsOnion() || pinnedAddresses is null || pinnedAddresses.Length == 0)
            {
                return CreateHttpClient(uri);
            }
            return new HttpClient(BuildPinnedHandler(pinnedAddresses), disposeHandler: true);
        }

        // The ConnectCallback pin closes the TOCTOU window between our
        // guard-time `Dns.GetHostAddressesAsync` and the runtime's own
        // connect-time DNS lookup. Without the pin an attacker can respond
        // with a public IP on the first resolve (passes our filter) and a
        // private IP on the second resolve (a "DNS rebinding" attack against
        // the intervening cache TTL). Because our callback uses the already-
        // validated `pinnedAddresses` and never re-resolves `DnsEndPoint.Host`,
        // the second lookup is skipped entirely. `AllowAutoRedirect = false`
        // is the other half: prevents a `302 Location: http://127.0.0.1/`
        // response from silently escaping the guard on the next hop.
        internal static SocketsHttpHandler BuildPinnedHandler(IPAddress[] pinnedAddresses)
        {
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectCallback = async (context, ct) =>
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(pinnedAddresses, context.DnsEndPoint.Port, ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
        }

        // Resolves DnsSafeHost and rejects any answer whose address family we
        // treat as non-public: loopback, RFC1918, IPv4 link-local (169.254/16 -
        // catches cloud metadata 169.254.169.254), CGNAT (100.64/10 - catches
        // tailscale + carrier NAT), IPv4 multicast (224/4), IPv4 "this network"
        // (0/8), IPv6 link-local (fe80::/10), IPv6 unique-local (fc00::/7),
        // IPv6 site-local (deprecated), IPv6 multicast. Returns the resolved
        // address set so the caller can pin the outbound socket. A literal IP in
        // the host is fed through IPAddress.Parse and the same filter, so a raw
        // http://127.0.0.1/ URL is caught even though it never touches DNS.
        internal static async Task<(bool safe, IPAddress[] addresses, string reason)>
            ResolveAndValidateAsync(Uri uri, CancellationToken cancellationToken)
        {
            var host = uri.DnsSafeHost;
            if (string.IsNullOrEmpty(host))
            {
                return (false, null, "LNURL host is empty");
            }
            if (Extensions.IsLocalNetwork(host))
            {
                return (false, null, "LNURL must not point to a local or private network host");
            }
            IPAddress[] resolved;
            try
            {
                if (IPAddress.TryParse(host, out var literal))
                {
                    resolved = new[] { literal };
                }
                else
                {
                    resolved = await Dns.GetHostAddressesAsync(host, cancellationToken);
                }
            }
            catch (Exception e)
            {
                return (false, null, $"Could not resolve LNURL host: {e.Message}");
            }
            if (resolved is null || resolved.Length == 0)
            {
                return (false, null, "LNURL host has no resolvable addresses");
            }
            foreach (var ip in resolved)
            {
                if (IsNonPublicAddress(ip))
                {
                    return (false, null, "LNURL host resolves to a local or private network address");
                }
            }
            return (true, resolved, null);
        }

        internal static bool IsNonPublicAddress(IPAddress ip)
        {
            var addr = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
            if (IPAddress.IsLoopback(addr))
            {
                return true;
            }
            // Unspecified address ("::" or "0.0.0.0") - some stacks fall back to
            // loopback resolution when connecting to it, so a caller-supplied
            // http://[::]/ can escape a naive filter.
            if (addr.Equals(IPAddress.Any) || addr.Equals(IPAddress.IPv6Any))
            {
                return true;
            }
            if (addr.AddressFamily == AddressFamily.InterNetwork)
            {
                if (addr.IsLocal() || addr.IsRFC1918())
                {
                    return true;
                }
                var b = addr.GetAddressBytes();
                if (b[0] == 0) return true;                                // 0.0.0.0/8 "this network"
                if (b[0] == 169 && b[1] == 254) return true;               // 169.254.0.0/16 link-local (incl. cloud metadata)
                if (b[0] == 100 && (b[1] & 0xC0) == 0x40) return true;     // 100.64.0.0/10 CGNAT / tailscale
                if (b[0] >= 224 && b[0] < 240) return true;                // 224.0.0.0/4 multicast
                if (b[0] >= 240) return true;                              // 240.0.0.0/4 reserved
                return false;
            }
            if (addr.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (addr.IsIPv6LinkLocal || addr.IsIPv6SiteLocal || addr.IsIPv6Multicast)
                {
                    return true;
                }
                var b = addr.GetAddressBytes();
                if ((b[0] & 0xFE) == 0xFC) return true;                    // fc00::/7 unique-local
                return false;
            }
            return false;
        }
    }
}
