using System;
using System.Collections.Generic;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class LNURLPayPaymentType : LightningPaymentType
    {
        public new static LNURLPayPaymentType Instance { get; } = new LNURLPayPaymentType();
        public override string ToPrettyString() => "LNURL-Pay";
        public override string GetId() => "LNURLPAY";
        public override string ToStringNormalized() => "LNURLPAY";
        public override IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<LNURLPayPaymentMethodDetails>(str);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network,
            JToken value)
        {
            return JsonConvert.DeserializeObject<LNURLPaySupportedPaymentMethod>(value.ToString());
        }

        public override string GetPaymentLink(BTCPayNetworkBase network, IPaymentMethodDetails paymentMethodDetails,
            Money cryptoInfoDue, string serverUri)
        {
            if (!paymentMethodDetails.Activated)
            {
                return null;
            }
            var lnurlPaymentMethodDetails = (LNURLPayPaymentMethodDetails)paymentMethodDetails;
            var uri = new Uri(
                $"{serverUri.WithTrailingSlash()}{network.CryptoCode}/lnurl/pay/i/{lnurlPaymentMethodDetails.BTCPayInvoiceId}");
            return LNURL.LNURL.EncodeUri(uri, "payRequest", lnurlPaymentMethodDetails.Bech32Mode).ToString();
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Lightning/ViewLightningLikePaymentData";
        public override object GetGreenfieldData(ISupportedPaymentMethod supportedPaymentMethod, bool canModifyStore)
        {
            if (supportedPaymentMethod is LNURLPaySupportedPaymentMethod lightningSupportedPaymentMethod)
                return new LNURLPayPaymentMethodBaseData()
                {
                    UseBech32Scheme = lightningSupportedPaymentMethod.UseBech32Scheme,
                    EnableForStandardInvoices = lightningSupportedPaymentMethod.EnableForStandardInvoices,
                    LUD12Enabled = lightningSupportedPaymentMethod.LUD12Enabled
                };
            return null;
        }

        public override bool IsPaymentType(string paymentType)
        {
            return IsPaymentTypeBase(paymentType);
        }

        public override void PopulateCryptoInfo(PaymentMethod details, InvoiceCryptoInfo invoiceCryptoInfo, string serverUrl)
        {
            invoiceCryptoInfo.PaymentUrls = new InvoiceCryptoInfo.InvoicePaymentUrls()
            {
                AdditionalData = new Dictionary<string, JToken>()
                {
                    {"LNURLP", JToken.FromObject(GetPaymentLink(details.Network, details.GetPaymentMethodDetails(), invoiceCryptoInfo.Due,
                        serverUrl))}
                }
            };
        }
    }
}
