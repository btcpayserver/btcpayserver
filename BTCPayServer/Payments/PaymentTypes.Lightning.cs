using System;
using System.Globalization;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class LightningPaymentType : PaymentType
    {
        public static LightningPaymentType Instance { get; } = new LightningPaymentType();

        private protected LightningPaymentType() { }

        public override string ToPrettyString() => "Off-Chain";
        public override string GetId() => "LN";
        public override string ToStringNormalized() => "LN";

        public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
        {
            return ((BTCPayNetwork)network)?.ToObject<LightningLikePaymentData>(str);
        }

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return ((BTCPayNetwork)network).ToString(paymentData);
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Lightning/ViewLightningLikePaymentData";

        public override bool IsPaymentType(string paymentType)
        {
            return paymentType?.Equals("offchain", StringComparison.InvariantCultureIgnoreCase) is true || base.IsPaymentType(paymentType);
        }
    }
}
