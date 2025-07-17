using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices;

public class OnChainRateTrackerHostedService(
    EventAggregator eventAggregator,
    Logs logger,
    WalletRepository walletRepository,
    DefaultRulesCollection defaultRateRules,
    RateFetcher rateFetcher,
    StoreRepository storeRepository) : EventHostedServiceBase(eventAggregator, logger)
{
    protected override void SubscribeToEvents()
    {
        Subscribe<NewOnChainTransactionEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is NewOnChainTransactionEvent newOnChainTransactionEvent)
            await ProcessEventCore(newOnChainTransactionEvent, cancellationToken);
    }

    private async Task ProcessEventCore(NewOnChainTransactionEvent transactionEvent, CancellationToken cancellationToken)
    {
        var derivation = transactionEvent.NewTransactionEvent.DerivationStrategy;
            if (derivation is null)
                return;
            var now = DateTimeOffset.UtcNow;
            // Too late
            if ((transactionEvent.NewTransactionEvent.TransactionData.Timestamp - now).Duration() > TimeSpan.FromMinutes(10))
                return;
            var cryptoCode = transactionEvent.NewTransactionEvent.CryptoCode;

            var stores = await storeRepository.GetStoresFromDerivation(transactionEvent.PaymentMethodId, derivation);
            foreach (var storeId in stores)
            {
                var store = await storeRepository.FindStore(storeId);
                if (store is null)
                    continue;
                var blob = store.GetStoreBlob();
                var trackedCurrencies = blob.GetTrackedRates();
                var rules = blob.GetRateRules(defaultRateRules);
                var fetching = rateFetcher.FetchRates(
                    trackedCurrencies
                        .Select(t => new CurrencyPair(cryptoCode, t))
                        .ToHashSet(), rules, new StoreIdRateContext(storeId), CancellationToken);
                JObject rates = new();
                foreach (var rate in fetching)
                {
                    var result = await rate.Value;
                    if (result.BidAsk is { } ba)
                        rates.Add(rate.Key.Right, ba.Center.ToString(CultureInfo.InvariantCulture));
                }
                if (!rates.Properties().Any())
                    continue;

                var wid = new WalletId(storeId, cryptoCode);
                var txObject = new WalletObjectId(wid, WalletObjectData.Types.Tx, transactionEvent.NewTransactionEvent.TransactionData.TransactionHash.ToString());

                await walletRepository.AddOrUpdateWalletObjectData(txObject, new WalletRepository.UpdateOperation.MergeObject(new JObject()
                {
                    [ "rates" ] = rates,
                }));
            }
    }
}
