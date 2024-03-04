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
        public override string GetId() => "CHAIN";
        public override string ToStringNormalized() => "CHAIN";
        public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
        {
            return ((BTCPayNetwork)network)?.ToObject<BitcoinLikePaymentData>(str);
        }

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return ((BTCPayNetwork)network).ToString(paymentData);
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Bitcoin/ViewBitcoinLikePaymentData";
        public override bool IsPaymentType(string paymentType)
        {
            return string.IsNullOrEmpty(paymentType) || base.IsPaymentType(paymentType);
        }
    }
}
