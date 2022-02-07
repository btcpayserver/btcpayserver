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
            var result = paymentRequestData.Blob == null
                ? new PaymentRequestBaseData()
                : ParseBlob(paymentRequestData.Blob);
            return result;
        }

        private static PaymentRequestBaseData ParseBlob(byte[] blob)
        {
            var jobj = JObject.Parse(ZipUtils.Unzip(blob));
            // Fixup some legacy payment requests
            if (jobj["expiryDate"].Type == JTokenType.Date)
                jobj["expiryDate"] = new JValue(NBitcoin.Utils.DateTimeToUnixTime(jobj["expiryDate"].Value<DateTime>()));
            return jobj.ToObject<PaymentRequestBaseData>();
        }

        public static bool SetBlob(this PaymentRequestData paymentRequestData, PaymentRequestBaseData blob)
        {
            var original = new Serializer(null).ToString(paymentRequestData.GetBlob());
            var newBlob = new Serializer(null).ToString(blob);
            if (original == newBlob)
                return false;
            paymentRequestData.Blob = ZipUtils.Zip(newBlob);
            return true;
        }
    }
}
