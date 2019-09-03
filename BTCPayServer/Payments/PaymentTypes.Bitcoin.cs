using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class BitcoinPaymentType : PaymentType
    {
        public static BitcoinPaymentType Instance { get; } = new BitcoinPaymentType();
        private BitcoinPaymentType()
        {

        }

        public override string ToPrettyString() => "On-Chain";
        public override string GetId() => "BTCLike";

        public override CryptoPaymentData DeserializePaymentData(string str, params object[] additionalData)
        {
            var result = JsonConvert.DeserializeObject<BitcoinLikePaymentData>(str);
            result.Network = (BTCPayNetwork)additionalData[0];
            return result;
		}

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return ((BTCPayNetwork) network).ToString(paymentData);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(string str)
        {
            return JsonConvert.DeserializeObject<Payments.Bitcoin.BitcoinLikeOnChainPaymentMethod>(str);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkProvider networkProvider, PaymentMethodId paymentMethodId, JToken value)
        {
            var network = networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (value is JObject jobj)
            {
                var scheme = network.NBXplorerNetwork.Serializer.ToObject<DerivationSchemeSettings>(jobj);
                scheme.Network = network;
                return scheme;
            }
            // Legacy
            return DerivationSchemeSettings.Parse(((JValue)value).Value<string>(), network);
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            if (txId == null)
                throw new ArgumentNullException(nameof(txId));
            if (network?.BlockExplorerLink == null)
                return null;
            txId = txId.Split('-').First();
            return string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, txId);
        }
        public override string InvoiceViewPaymentPartialName { get; } = "ViewBitcoinLikePaymentData";
        public override IEnumerable<CurrencyPair> GetCurrencyPairs(ISupportedPaymentMethod method, string targetCurrencyCode, StoreBlob storeBlob)
        {
            var result = new List<CurrencyPair> {new CurrencyPair(method.PaymentId.CryptoCode, targetCurrencyCode)};

            if (storeBlob.OnChainMinValue != null)
                result.Add(new CurrencyPair(method.PaymentId.CryptoCode, storeBlob.OnChainMinValue.Currency));
            return result;
        }
    }
}
