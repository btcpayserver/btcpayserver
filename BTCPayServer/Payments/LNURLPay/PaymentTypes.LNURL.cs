using System;
using System.Collections.Generic;
using System.Globalization;
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
        public new static LNURLPayPaymentType Instance { get; } = new();
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

        public override string GetPaymentLink(BTCPayNetworkBase network, InvoiceEntity invoice, IPaymentMethodDetails paymentMethodDetails,
            decimal cryptoInfoDue, string serverUri)
        {
            if (!paymentMethodDetails.Activated)
            {
                return null;
            }

            try
            {
                var lnurlPaymentMethodDetails = (LNURLPayPaymentMethodDetails)paymentMethodDetails;
                var uri = new Uri(
                    $"{serverUri.WithTrailingSlash()}{network.CryptoCode}/UILNURL/pay/i/{invoice.Id}");
                return LNURL.LNURL.EncodeUri(uri, "payRequest", lnurlPaymentMethodDetails.Bech32Mode).ToString();
            }
            catch (Exception e)
            {
                // TODO: we need to switch payment types from static singletons to DI
                // _logger.LogError(e, "Error generating LNURL payment link");
                Console.WriteLine($"Error generating LNURL payment link: {e.Message}");
                return null;
            }
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Lightning/ViewLightningLikePaymentData";
        public override object GetGreenfieldData(ISupportedPaymentMethod supportedPaymentMethod, bool canModifyStore)
        {
            if (supportedPaymentMethod is LNURLPaySupportedPaymentMethod lightningSupportedPaymentMethod)
                return new LNURLPayPaymentMethodBaseData()
                {
                    UseBech32Scheme = lightningSupportedPaymentMethod.UseBech32Scheme,
                    LUD12Enabled = lightningSupportedPaymentMethod.LUD12Enabled
                };
            return null;
        }

        public override bool IsPaymentType(string paymentType)
        {
            return IsPaymentTypeBase(paymentType);
        }

        public override void PopulateCryptoInfo(InvoiceEntity invoice, PaymentMethod details, InvoiceCryptoInfo invoiceCryptoInfo, string serverUrl)
        {
            invoiceCryptoInfo.PaymentUrls = new InvoiceCryptoInfo.InvoicePaymentUrls()
            {
                AdditionalData = new Dictionary<string, JToken>()
                {
                    {"LNURLP", JToken.FromObject(GetPaymentLink(details.Network, invoice, details.GetPaymentMethodDetails(), invoiceCryptoInfo.GetDue().Value,
                        serverUrl))}
                }
            };
        }
    }
}
