using System.Linq;
using BTCPayServer.Services.Stores;

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


        public bool TryGetChangellyClient(string storeId, out string error,
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
            if (blob.IsExcluded(ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId))
            {
                error = "Changelly not enabled for this store";
                return false;
            }

            var paymentMethod = (ChangellySupportedPaymentMethod)store
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .SingleOrDefault(method =>
                    method.PaymentId == ChangellySupportedPaymentMethod.ChangellySupportedPaymentMethodId);

            if (paymentMethod == null || !paymentMethod.IsConfigured())
            {
                error = "Changelly not configured for this store";
                return false;
            }

            changelly = new global::Changelly.Changelly(paymentMethod.ApiKey, paymentMethod.ApiSecret,
                paymentMethod.ApiUrl);
            error = null;
            return true;
        }
    }
}