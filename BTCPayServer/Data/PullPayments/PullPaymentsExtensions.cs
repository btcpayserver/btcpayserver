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
            var result = JsonConvert.DeserializeObject<PullPaymentBlob>(data.Blob);
            result!.SupportedPaymentMethods = result.SupportedPaymentMethods.Where(id => id is not null).ToArray();
            return result;
        }
        public static void SetBlob(this PullPaymentData data, PullPaymentBlob blob)
        {
            data.Blob = JsonConvert.SerializeObject(blob).ToString();
        }

        public static bool IsSupported(this PullPaymentData data, Payments.PaymentMethodId paymentId)
        {
            return data.GetBlob().SupportedPaymentMethods.Contains(paymentId);
        }
    }
}
