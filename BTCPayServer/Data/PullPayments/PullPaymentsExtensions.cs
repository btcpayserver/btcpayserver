using System.Linq;
using System.Text;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public static class PullPaymentsExtensions
    {
      
        public static PullPaymentBlob GetBlob(this PullPaymentData data)
        {
            return JsonConvert.DeserializeObject<PullPaymentBlob>(Encoding.UTF8.GetString(data.Blob));
        }
        public static void SetBlob(this PullPaymentData data, PullPaymentBlob blob)
        {
            data.Blob = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(blob));
        }

        public static bool IsSupported(this PullPaymentData data, BTCPayServer.Payments.PaymentMethodId paymentId)
        {
            return data.GetBlob().SupportedPaymentMethods.Contains(paymentId);
        }
    }
}
