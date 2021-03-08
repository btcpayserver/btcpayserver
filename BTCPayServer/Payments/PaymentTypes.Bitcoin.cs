using System;
using System.Globalization;
using System.Linq;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using BTCPayServer.BIP78.Sender;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class BitcoinPaymentType : PaymentType
    {
        public static BitcoinPaymentType Instance { get; } = new BitcoinPaymentType();
        
        private BitcoinPaymentType() { }

        public override string ToPrettyString() => "On-Chain";
        public override string GetId() => "BTCLike";
        public override string GetBadge() => "";
        public override string ToStringNormalized() => "OnChain";
        public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
        {
            return ((BTCPayNetwork)network)?.ToObject<BitcoinLikePaymentData>(str);
        }

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return ((BTCPayNetwork)network).ToString(paymentData);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str)
        {
            return ((BTCPayNetwork)network).ToObject<BitcoinLikeOnChainPaymentMethod>(str);
        }

        public override string SerializePaymentMethodDetails(BTCPayNetworkBase network, IPaymentMethodDetails details)
        {
            return ((BTCPayNetwork)network).ToString((BitcoinLikeOnChainPaymentMethod)details);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            var net = (BTCPayNetwork)network;
            if (value is JObject jobj)
            {
                var scheme = net.NBXplorerNetwork.Serializer.ToObject<DerivationSchemeSettings>(jobj);
                scheme.Network = net;
                return scheme;
            }
            // Legacy
            return DerivationSchemeSettings.Parse(((JValue)value).Value<string>(), net);
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

        public override string GetPaymentLink(BTCPayNetworkBase network, IPaymentMethodDetails paymentMethodDetails,
            Money cryptoInfoDue, string serverUri)
        {
            if (!paymentMethodDetails.Activated)
            {
                return string.Empty;
            }
            var bip21 = ((BTCPayNetwork)network).GenerateBIP21(paymentMethodDetails.GetPaymentDestination(), cryptoInfoDue);

            if ((paymentMethodDetails as BitcoinLikeOnChainPaymentMethod)?.PayjoinEnabled is true && serverUri != null)
            {
                bip21 += $"&{PayjoinClient.BIP21EndpointKey}={serverUri.WithTrailingSlash()}{network.CryptoCode}/{PayjoinClient.BIP21EndpointKey}";
            }
            return bip21;
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Bitcoin/ViewBitcoinLikePaymentData";
        public override bool IsPaymentType(string paymentType)
        {
            return string.IsNullOrEmpty(paymentType) || base.IsPaymentType(paymentType);
        }
    }
}
