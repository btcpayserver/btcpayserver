using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class LightningLikePayoutHandler : IPayoutHandler
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly StoreRepository _storeRepository;
        private readonly IOptions<LightningNetworkOptions> _options;
        private readonly LightningClientFactoryService _lightningClientFactoryService;
        private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;

        public LightningLikePayoutHandler(BTCPayNetworkProvider btcPayNetworkProvider, 
            ApplicationDbContextFactory applicationDbContextFactory,
            StoreRepository storeRepository,
            IOptions<LightningNetworkOptions> options,
            LightningClientFactoryService lightningClientFactoryService, 
            BTCPayNetworkJsonSerializerSettings jsonSerializerSettings)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _applicationDbContextFactory = applicationDbContextFactory;
            _storeRepository = storeRepository;
            _options = options;
            _lightningClientFactoryService = lightningClientFactoryService;
            _jsonSerializerSettings = jsonSerializerSettings;
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
            return Task.FromResult<IClaimDestination>(
                BOLT11PaymentRequest.TryParse(destination, out var invoice, network.NBitcoinNetwork)
                    ? new BoltInvoiceClaimDestination(destination, invoice)
                    : null);
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
