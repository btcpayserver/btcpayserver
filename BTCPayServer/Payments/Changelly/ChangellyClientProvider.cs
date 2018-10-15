using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Services.Stores;
using Changelly.ResponseModel;

namespace BTCPayServer.Payments.Changelly
{
    public class ChangellyClientProvider
    {
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;


        public ChangellyClientProvider(StoreRepository storeRepository, BTCPayNetworkProvider btcPayNetworkProvider)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
        }


        public virtual bool TryGetChangellyClient(string storeId, out string error,
            out global::Changelly.Changelly changelly)
        {
            changelly = null;


            var store = _storeRepository.FindStore(storeId).Result;
            if (store == null)
            {
                error = "Store not found";
                return false;
            }

            var blob = store.GetStoreBlob();
            var changellySettings = blob.ChangellySettings;

            
            if (changellySettings == null || !changellySettings.IsConfigured())
            {
                error = "Changelly not configured for this store";
                return false;
            }

            if (!changellySettings.Enabled)
            {
                error = "Changelly not enabled for this store";
                return false;
            }

            changelly = new global::Changelly.Changelly(changellySettings.ApiKey, changellySettings.ApiSecret,
                changellySettings.ApiUrl);
            error = null;
            return true;
        }
        
        public virtual (IList<CurrencyFull> currency, bool Success, string Error) GetCurrenciesFull(global::Changelly.Changelly client)
        {
            return client.GetCurrenciesFull();
        }

        public virtual (double amount, bool Success, string Error) GetExchangeAmount(global::Changelly.Changelly client,  string fromCurrency, string toCurrency,
            double amount)
        {

            return client.GetExchangeAmount(fromCurrency, toCurrency, amount);
        }
    }
}
