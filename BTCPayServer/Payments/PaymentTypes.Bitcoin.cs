using System;
using System.Globalization;
using System.Linq;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json.Linq;
using InvoiceCryptoInfo = BTCPayServer.Services.Invoices.InvoiceCryptoInfo;

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
            ArgumentNullException.ThrowIfNull(network);
            ArgumentNullException.ThrowIfNull(value);
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

        public override string GetPaymentLink(BTCPayNetworkBase network, InvoiceEntity invoice, IPaymentMethodDetails paymentMethodDetails,
            decimal cryptoInfoDue, string serverUri)
        {
            if (!paymentMethodDetails.Activated)
            {
                return string.Empty;
            }
            var bip21 = ((BTCPayNetwork)network).GenerateBIP21(paymentMethodDetails.GetPaymentDestination(), cryptoInfoDue);

            if ((paymentMethodDetails as BitcoinLikeOnChainPaymentMethod)?.PayjoinEnabled is true && serverUri != null)
            {
                bip21.QueryParams.Add(PayjoinClient.BIP21EndpointKey, $"{serverUri.WithTrailingSlash()}{network.CryptoCode}/{PayjoinClient.BIP21EndpointKey}");
            }
            return bip21.ToString();
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Bitcoin/ViewBitcoinLikePaymentData";
        public override object GetGreenfieldData(ISupportedPaymentMethod supportedPaymentMethod, bool canModifyStore)
        {
            if (supportedPaymentMethod is DerivationSchemeSettings derivationSchemeSettings)
                return new OnChainPaymentMethodBaseData()
                {
                    DerivationScheme = derivationSchemeSettings.AccountDerivation.ToString(),
                    AccountKeyPath = derivationSchemeSettings.GetSigningAccountKeySettings().GetRootedKeyPath(),
                    Label = derivationSchemeSettings.Label
                };
            return null;
        }

        public override bool IsPaymentType(string paymentType)
        {
            return string.IsNullOrEmpty(paymentType) || base.IsPaymentType(paymentType);
        }

        public override void PopulateCryptoInfo(InvoiceEntity invoice, PaymentMethod details, InvoiceCryptoInfo cryptoInfo,
            string serverUrl)
        {
            cryptoInfo.PaymentUrls = new InvoiceCryptoInfo.InvoicePaymentUrls()
            {
                BIP21 = GetPaymentLink(details.Network, invoice, details.GetPaymentMethodDetails(), cryptoInfo.GetDue().Value, serverUrl),
            };
        }
    }
}
