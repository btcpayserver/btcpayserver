using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Validation;
using LNURL;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class LightningLikePayoutHandler : IPayoutHandler
    {
        public const string LightningLikePayoutHandlerOnionNamedClient =
            nameof(LightningLikePayoutHandlerOnionNamedClient);

        public const string LightningLikePayoutHandlerClearnetNamedClient =
            nameof(LightningLikePayoutHandlerClearnetNamedClient);

        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly IHttpClientFactory _httpClientFactory;

        public LightningLikePayoutHandler(BTCPayNetworkProvider btcPayNetworkProvider,
            IHttpClientFactory httpClientFactory)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _httpClientFactory = httpClientFactory;
        }

        public bool CanHandle(PaymentMethodId paymentMethod)
        {
            return paymentMethod.PaymentType == LightningPaymentType.Instance &&
                   _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethod.CryptoCode)?.SupportLightning is true;
        }

        public Task TrackClaim(PaymentMethodId paymentMethodId, IClaimDestination claimDestination)
        {
            return Task.CompletedTask;
        }

        public HttpClient CreateClient(Uri uri)
        {
            return _httpClientFactory.CreateClient(uri.IsOnion()
                ? LightningLikePayoutHandlerOnionNamedClient
                : LightningLikePayoutHandlerClearnetNamedClient);
        }

        public async Task<(IClaimDestination destination, string error)> ParseClaimDestination(PaymentMethodId paymentMethodId, string destination, bool validate)
        {
            destination = destination.Trim();
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            try
            {
                string lnurlTag = null;
                var lnurl = EmailValidator.IsEmail(destination)
                    ? LNURL.LNURL.ExtractUriFromInternetIdentifier(destination)
                    : LNURL.LNURL.Parse(destination, out lnurlTag);

                if (lnurlTag is null)
                {
                    var info = (LNURLPayRequest)(await LNURL.LNURL.FetchInformation(lnurl, CreateClient(lnurl)));
                    lnurlTag = info.Tag;
                }

                if (lnurlTag.Equals("payRequest", StringComparison.InvariantCultureIgnoreCase))
                {
                    return (new LNURLPayClaimDestinaton(destination), null);
                }
            }
            catch (FormatException)
            {
            }
            catch
            {
                return (null, "The LNURL / Lightning Address provided was not online.");
            }

            var result =
                BOLT11PaymentRequest.TryParse(destination, out var invoice, network.NBitcoinNetwork)
                    ? new BoltInvoiceClaimDestination(destination, invoice)
                    : null;

            if (result == null) return (null, "A valid BOLT11 invoice (with 30+ day expiry) or LNURL Pay or Lightning address was not provided.");
            if (validate && (invoice.ExpiryDate.UtcDateTime - DateTime.UtcNow).Days < 30)
            {
                return (null,
                    $"The BOLT11 invoice must have an expiry date of at least 30 days from submission (Provided was only {(invoice.ExpiryDate.UtcDateTime - DateTime.UtcNow).Days}).");
            }
            if (invoice.ExpiryDate.UtcDateTime < DateTime.UtcNow)
            {
                return (null,
                    "The BOLT11 invoice submitted has expired.");
            }

            return (result, null);
        }

        public IPayoutProof ParseProof(PayoutData payout)
        {
            return null;
        }

        public void StartBackgroundCheck(Action<Type[]> subscribe)
        {
        }

        public Task BackgroundCheck(object o)
        {
            return Task.CompletedTask;
        }

        public Task<decimal> GetMinimumPayoutAmount(PaymentMethodId paymentMethodId, IClaimDestination claimDestination)
        {
            return Task.FromResult(Money.Satoshis(1).ToDecimal(MoneyUnit.BTC));
        }

        public Dictionary<PayoutState, List<(string Action, string Text)>> GetPayoutSpecificActions()
        {
            return new Dictionary<PayoutState, List<(string Action, string Text)>>();
        }

        public Task<StatusMessageModel> DoSpecificAction(string action, string[] payoutIds, string storeId)
        {
            return Task.FromResult<StatusMessageModel>(null);
        }

        public IEnumerable<PaymentMethodId> GetSupportedPaymentMethods()
        {
            return _btcPayNetworkProvider.GetAll().OfType<BTCPayNetwork>().Where(network => network.SupportLightning)
                .Select(network => new PaymentMethodId(network.CryptoCode, LightningPaymentType.Instance));
        }

        public Task<IActionResult> InitiatePayment(PaymentMethodId paymentMethodId, string[] payoutIds)
        {
            return Task.FromResult<IActionResult>(new RedirectToActionResult("ConfirmLightningPayout",
                "LightningLikePayout", new { cryptoCode = paymentMethodId.CryptoCode, payoutIds }));
        }
    }
}
