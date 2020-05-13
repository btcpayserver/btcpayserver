using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class StoreDataExtensions
    {
#pragma warning disable CS0618
        public static PaymentMethodId GetDefaultPaymentId(this StoreData storeData, BTCPayNetworkProvider networks)
        {
            PaymentMethodId[] paymentMethodIds = storeData.GetEnabledPaymentIds(networks);

            var defaultPaymentId = string.IsNullOrEmpty(storeData.DefaultCrypto) ? null : PaymentMethodId.Parse(storeData.DefaultCrypto);
            var chosen = paymentMethodIds.FirstOrDefault(f => f == defaultPaymentId) ??
                         paymentMethodIds.FirstOrDefault(f => f.CryptoCode == defaultPaymentId?.CryptoCode) ??
                         paymentMethodIds.FirstOrDefault();
            return chosen;
        }

        public static PaymentMethodId[] GetEnabledPaymentIds(this StoreData storeData, BTCPayNetworkProvider networks)
        {
            var excludeFilter = storeData.GetStoreBlob().GetExcludedPaymentMethods();
            var paymentMethodIds = storeData.GetSupportedPaymentMethods(networks).Select(p => p.PaymentId)
                                .Where(a => !excludeFilter.Match(a))
                                .OrderByDescending(a => a.CryptoCode == "BTC")
                                .ThenBy(a => a.CryptoCode)
                                .ThenBy(a => a.PaymentType == PaymentTypes.LightningLike ? 1 : 0)
                                .ToArray();
            return paymentMethodIds;
        }

        public static void SetDefaultPaymentId(this StoreData storeData, PaymentMethodId defaultPaymentId)
        {
            storeData.DefaultCrypto = defaultPaymentId.ToString();
        }
#pragma warning restore CS0618

        
        public static StoreBlob GetStoreBlob(this StoreData storeData)
        {
            var result = storeData.StoreBlob == null ? new StoreBlob() : new Serializer(null).ToObject<StoreBlob>(Encoding.UTF8.GetString(storeData.StoreBlob));
            if (result.PreferredExchange == null)
                result.PreferredExchange = CoinGeckoRateProvider.CoinGeckoName;
            return result;
        }

        public static bool SetStoreBlob(this StoreData storeData, StoreBlob storeBlob)
        {
            var original = new Serializer(null).ToString(storeData.GetStoreBlob());
            var newBlob = new Serializer(null).ToString(storeBlob);
            if (original == newBlob)
                return false;
            storeData.StoreBlob = Encoding.UTF8.GetBytes(newBlob);
            return true;
        }

        public static IEnumerable<ISupportedPaymentMethod> GetSupportedPaymentMethods(this StoreData storeData,
            BTCPayNetworkProvider btcPayNetworkProvider)
        {
            return storeData.StoreWalletDatas.Select(data => data.WalletData.GetBlob(btcPayNetworkProvider));
        }

        public static IEnumerable<ISupportedPaymentMethod> GetSupportedPaymentMethodsLegacy(this StoreData storeData, BTCPayNetworkProvider networks)
        {
            if (storeData == null)
                throw new ArgumentNullException(nameof(storeData));
            networks = networks.UnfilteredNetworks;
#pragma warning disable CS0618
            bool btcReturned = false;

            // Legacy stuff which should go away
            if (!string.IsNullOrEmpty(storeData.DerivationStrategy))
            {
                btcReturned = true;
                yield return DerivationSchemeSettings.Parse(storeData.DerivationStrategy, networks.BTC);
            }


            if (!string.IsNullOrEmpty(storeData.DerivationStrategies))
            {
                JObject strategies = JObject.Parse(storeData.DerivationStrategies);
                foreach (var strat in strategies.Properties())
                {
                    var paymentMethodId = PaymentMethodId.Parse(strat.Name);
                    var network = networks.GetNetwork<BTCPayNetworkBase>(paymentMethodId.CryptoCode);
                    if (network != null)
                    {
                        if (network == networks.BTC && paymentMethodId.PaymentType == PaymentTypes.BTCLike && btcReturned)
                            continue;
                        if (strat.Value.Type == JTokenType.Null)
                            continue;
                        yield return
                            paymentMethodId.PaymentType.DeserializeSupportedPaymentMethod(network, strat.Value);
                    }
                }
            }
#pragma warning restore CS0618
        }

       
    }
}
