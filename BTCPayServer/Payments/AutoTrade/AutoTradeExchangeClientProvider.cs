using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using BTCPayServer.Services.Stores;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments.AutoTrade.BitBank;
using NBitcoin;
using System;

namespace BTCPayServer.Payments.AutoTrade
{
    public class AutoTradeExchangeClientProvider
    {
        public static List<string> GetAllSupportedExchangeNames() => new List<string> { "BitBank" };

        public AutoTradeExchangeSettingsBase GetSettings(string name)
        {
            switch (name)
            {
                case "BitBank":
                    return new BitBankSettings();
                default:
                    throw new ArgumentException($"Unknown exchange name {name}");
            }
        }

        private readonly ConcurrentDictionary<string, IAutoTradeExchangeClient> _clientCache = new ConcurrentDictionary<string, IAutoTradeExchangeClient>();

        private StoreRepository _storeRepository { get; }
        private IHttpClientFactory _httpClientFactory { get; }

        public AutoTradeExchangeClientProvider(StoreRepository storeRepository, IHttpClientFactory httpClientFactory)
        {
            _storeRepository = storeRepository;
            _httpClientFactory = httpClientFactory;
        }

        public void InvalidateClient(string storeId, string clientType)
        {
            if (_clientCache.ContainsKey(clientType))
            {
                _clientCache.Remove(clientType, out var value);
            }
        }
        public virtual async Task<IAutoTradeExchangeClient> TryGetClient(string storeId, string exchangeName, StoreData storeData = null)
        {
            if (_clientCache.ContainsKey(storeId))
            {
                return _clientCache[storeId];
            }

            if (storeData == null)
            {
                storeData = await _storeRepository.FindStore(storeId);
                if (storeData == null)
                {
                    throw new AutoTradeException("Store not found");
                }
            }

            var blob = storeData.GetStoreBlob();
            var autoTradeSettings = blob.AutoTradeExchangeSettings;
            if (autoTradeSettings == null || !autoTradeSettings.IsConfigured())
            {
                throw new AutoTradeException("AutoTrading Feature is not configured for this store.");
            }
            if (!autoTradeSettings.Enabled)
            {
                throw new AutoTradeException("AutoTrading Not enabled in this store.");
            }

            switch (exchangeName)
            {
                case "BitBank":
                    var client = new BitBankClient(
                        autoTradeSettings.ApiKey,
                        autoTradeSettings.ApiSecret,
                        autoTradeSettings.ApiUrl);
                    _clientCache.AddOrReplace(storeId, client);
                    return client;
                default:
                    throw new AutoTradeException($"Unknown Exchange Name {exchangeName} !");
            }
        }
    }
}
