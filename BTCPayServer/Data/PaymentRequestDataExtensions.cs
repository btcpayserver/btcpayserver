using System;
using BTCPayServer.Client.Models;
using NBXplorer;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class PaymentRequestDataExtensions
    {
        public static PaymentRequestBaseData GetBlob(this PaymentRequestData paymentRequestData)
        {
            if (paymentRequestData.Blob2 is not null)
            {
                return paymentRequestData.HasTypedBlob<PaymentRequestBaseData>().GetBlob();
            }
#pragma warning disable CS0618 // Type or member is obsolete
            else if (paymentRequestData.Blob is not null)
            {
                return ParseBlob(paymentRequestData.Blob);
            }
#pragma warning restore CS0618 // Type or member is obsolete
            return new PaymentRequestBaseData();
        }

        static PaymentRequestBaseData ParseBlob(byte[] blob)
        {
            var jobj = JObject.Parse(ZipUtils.Unzip(blob));
            // Fixup some legacy payment requests
            if (jobj["expiryDate"].Type == JTokenType.Date)
                jobj["expiryDate"] = new JValue(NBitcoin.Utils.DateTimeToUnixTime(jobj["expiryDate"].Value<DateTime>()));
            return jobj.ToObject<PaymentRequestBaseData>();
        }

        public static void SetBlob(this PaymentRequestData paymentRequestData, PaymentRequestBaseData blob)
        {
            paymentRequestData.HasTypedBlob<PaymentRequestBaseData>().SetBlob(blob);
        }
    }
}
