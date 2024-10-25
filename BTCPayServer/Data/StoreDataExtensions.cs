#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json.Linq;
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace BTCPayServer.Data
{
    public static class StoreDataExtensions
    {

#pragma warning disable CS0618
        public static PaymentMethodId? GetDefaultPaymentId(this StoreData storeData)
        {
            PaymentMethodId.TryParse(storeData.DefaultCrypto, out var defaultPaymentId);
            return defaultPaymentId;
        }

        public static PaymentMethodId[] GetEnabledPaymentIds(this StoreData storeData)
        {
            return storeData.GetPaymentMethodConfigs(true).Select(c => c.Key).ToArray();
        }

        public static Dictionary<PaymentMethodId, object> GetEnabledPaymentMethods(this StoreData storeData, PaymentMethodHandlerDictionary handlers)
        {
            return storeData.GetPaymentMethodConfigs(true)
                .Where(m => handlers.Support(m.Key))
                .OrderByDescending(a => a.Key.ToString() == "BTC")
                .ThenBy(a => a.Key.ToString())
                .ThenBy(a => handlers[a.Key].ParsePaymentMethodConfig(a.Value) is LightningPaymentMethodConfig ? 1 : 0)
                .ToDictionary(a => a.Key, a => handlers[a.Key].ParsePaymentMethodConfig(a.Value));
        }

        public static void SetDefaultPaymentId(this StoreData storeData, PaymentMethodId? defaultPaymentId)
        {
            storeData.DefaultCrypto = defaultPaymentId?.ToString();
        }
#pragma warning restore CS0618


        public static StoreBlob GetStoreBlob(this StoreData storeData)
        {
            ArgumentNullException.ThrowIfNull(storeData);
            var result = storeData.StoreBlob == null ? new StoreBlob() : new Serializer(null).ToObject<StoreBlob>(storeData.StoreBlob);
            if (result.PaymentMethodCriteria is null)
                result.PaymentMethodCriteria = new List<PaymentMethodCriteria>();
            result.PaymentMethodCriteria.RemoveAll(criteria => criteria?.PaymentMethod is null);
            return result;
        }

        public static bool AnyPaymentMethodAvailable(this StoreData storeData, PaymentMethodHandlerDictionary handlers)
        {
            return storeData.GetPaymentMethodConfigs(handlers, true).Any();
        }

        public static bool SetStoreBlob(this StoreData storeData, StoreBlob storeBlob)
        {
            var original = new Serializer(null).ToString(storeData.GetStoreBlob());
            var newBlob = new Serializer(null).ToString(storeBlob);
            if (original == newBlob)
                return false;
            storeData.StoreBlob = newBlob;
            return true;
        }

        public static object? GetPaymentMethodConfig(this StoreData storeData, Payments.PaymentMethodId paymentMethodId, PaymentMethodHandlerDictionary handlers, bool onlyEnabled = false)
        {
            var config = GetPaymentMethodConfig(storeData, paymentMethodId, onlyEnabled);
            if (config is null || !handlers.Support(paymentMethodId))
                return null;
            return handlers[paymentMethodId].ParsePaymentMethodConfig(config);
        }
        public static JToken? GetPaymentMethodConfig(this StoreData storeData, Payments.PaymentMethodId paymentMethodId, bool onlyEnabled = false)
        {
            if (string.IsNullOrEmpty(storeData.DerivationStrategies))
                return null;
            if (!onlyEnabled)
            {
                JObject strategies = JObject.Parse(storeData.DerivationStrategies);
                return strategies[paymentMethodId.ToString()];
            }
            else
            {
                var excludeFilter = storeData.GetStoreBlob().GetExcludedPaymentMethods();
                JObject strategies = JObject.Parse(storeData.DerivationStrategies);
                return excludeFilter.Match(paymentMethodId) ? null : strategies[paymentMethodId.ToString()];
            }
        }
        public static T? GetPaymentMethodConfig<T>(this StoreData storeData, Payments.PaymentMethodId paymentMethodId, PaymentMethodHandlerDictionary handlers, bool onlyEnabled = false) where T : class
        {
            var conf = storeData.GetPaymentMethodConfig(paymentMethodId, onlyEnabled);
            if (conf is null)
                return default;
            return handlers[paymentMethodId].ParsePaymentMethodConfig(conf) as T;
        }

        public static void SetPaymentMethodConfig(this StoreData storeData, IPaymentMethodHandler handler, object? config)
        {
            storeData.SetPaymentMethodConfig(handler.PaymentMethodId, config is null ? null : JToken.FromObject(config, handler.Serializer));
        }
        public static void SetPaymentMethodConfig(this StoreData storeData, PaymentMethodId paymentMethodId, JToken? config)
        {
            JObject strategies = string.IsNullOrEmpty(storeData.DerivationStrategies) ? new JObject() : JObject.Parse(storeData.DerivationStrategies);
            if (config is null)
                strategies.Remove(paymentMethodId.ToString());
            else
                strategies[paymentMethodId.ToString()] = config;
            storeData.DerivationStrategies = strategies.ToString(Newtonsoft.Json.Formatting.None);
        }
        public static Dictionary<PaymentMethodId, object> GetPaymentMethodConfigs(this StoreData storeData, PaymentMethodHandlerDictionary handlers, bool onlyEnabled = false)
        {
            return storeData.GetPaymentMethodConfigs(onlyEnabled)
                .Where(h => handlers.Support(h.Key))
                .ToDictionary(c => c.Key, c => handlers[c.Key].ParsePaymentMethodConfig(c.Value));
        }
        public static Dictionary<PaymentMethodId, T> GetPaymentMethodConfigs<T>(this StoreData storeData, PaymentMethodHandlerDictionary handlers, bool onlyEnabled = false) where T : class
        {
            return storeData.GetPaymentMethodConfigs(onlyEnabled)
                .Select(h => (h.Key, Config: handlers.TryGetValue(h.Key, out var handler) ? handler.ParsePaymentMethodConfig(h.Value) as T : null))
                .Where(h => h.Config is not null)
                .ToDictionary(c => c.Key, c => c.Config!);
        }
        public static Dictionary<PaymentMethodId, JToken> GetPaymentMethodConfigs(this StoreData storeData, bool onlyEnabled = false)
        {
            if (string.IsNullOrEmpty(storeData.DerivationStrategies))
                return new Dictionary<PaymentMethodId, JToken>();
            var excludeFilter = onlyEnabled ? storeData.GetStoreBlob().GetExcludedPaymentMethods() : null;
            var paymentMethodConfigurations = new Dictionary<PaymentMethodId, JToken>();
            JObject strategies = JObject.Parse(storeData.DerivationStrategies);
            foreach (var strat in strategies.Properties())
            {
                if (!PaymentMethodId.TryParse(strat.Name, out var paymentMethodId))
                    continue;
                if (excludeFilter?.Match(paymentMethodId) is true)
                    continue;
                paymentMethodConfigurations.Add(paymentMethodId, strat.Value);
            }
            return paymentMethodConfigurations;
        }

        public static bool IsLightningEnabled(this StoreData storeData, string cryptoCode)
        {
            return IsPaymentMethodEnabled(storeData, PaymentTypes.LN.GetPaymentMethodId(cryptoCode));
        }

        public static bool IsLNUrlEnabled(this StoreData storeData, string cryptoCode)
        {
            return IsPaymentMethodEnabled(storeData, PaymentTypes.LNURL.GetPaymentMethodId(cryptoCode));
        }

        private static bool IsPaymentMethodEnabled(this StoreData storeData, PaymentMethodId paymentMethodId)
        {
            return storeData.GetPaymentMethodConfig(paymentMethodId, true) is not null;
        }
    }
}
