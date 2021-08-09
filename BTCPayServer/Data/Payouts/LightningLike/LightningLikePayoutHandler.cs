using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class LightningLikePayoutHandler : IPayoutHandler
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;

        public LightningLikePayoutHandler(BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
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

        public Task<IClaimDestination> ParseClaimDestination(PaymentMethodId paymentMethodId, string destination)
        {
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            destination = destination.Trim();
            var result = 
                BOLT11PaymentRequest.TryParse(destination, out var invoice, network.NBitcoinNetwork)
                    ? new BoltInvoiceClaimDestination(destination, invoice)
                    : null;

            if (result == null) return Task.FromResult<IClaimDestination>(result);
            return (invoice.ExpiryDate.UtcDateTime - DateTime.UtcNow).Days < 30 ? null : Task.FromResult<IClaimDestination>(result);
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
