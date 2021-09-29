using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.PayJoin.Sender;
using LNURL;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class LightningLikePayoutHandler : IPayoutHandler
    {
        public const string LightningLikePayoutHandlerOnionNamedClient = nameof(LightningLikePayoutHandlerOnionNamedClient);
        public const string LightningLikePayoutHandlerClearnetNamedClient = nameof(LightningLikePayoutHandlerClearnetNamedClient);
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly IHttpClientFactory _httpClientFactory;

        public LightningLikePayoutHandler(BTCPayNetworkProvider btcPayNetworkProvider, IHttpClientFactory httpClientFactory)
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
            return _httpClientFactory.CreateClient(uri.IsOnion() ? LightningLikePayoutHandlerOnionNamedClient : LightningLikePayoutHandlerClearnetNamedClient);
        }

        public async Task<IClaimDestination> ParseClaimDestination(PaymentMethodId paymentMethodId, string destination)
        {
            destination = destination.Trim();
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            try
            {
               var lnurl = LNURL.LNURL.Parse(destination, out var lnurlTag);
               if (lnurlTag is null)
               {
                   var info = (await LNURL.LNURL.FetchInformation(lnurl, "payRequest", CreateClient(lnurl))) as LNURLPayRequest;
                   lnurlTag = info.Tag;
               }

               if (lnurlTag.Equals("payRequest"))
               {
                   return new LNURLPayClaimDestinaton(destination);
               }
            }
            catch (FormatException)
            {
            }
            
            var result = 
                BOLT11PaymentRequest.TryParse(destination, out var invoice, network.NBitcoinNetwork)
                    ? new BoltInvoiceClaimDestination(destination, invoice)
                    : null;

            if (result == null) return null;
            return (invoice.ExpiryDate.UtcDateTime - DateTime.UtcNow).Days < 30 ? null : result;
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
            return  Task.FromResult<IActionResult>(new RedirectToActionResult("ConfirmLightningPayout", "LightningLikePayout", new {cryptoCode =  paymentMethodId.CryptoCode, payoutIds}));
        }
    }
}
