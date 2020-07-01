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
                : JObject.Parse(ZipUtils.Unzip(paymentRequestData.Blob)).ToObject<PaymentRequestBaseData>();
            return result;
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
