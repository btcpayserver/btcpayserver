using System.Linq;
using System.Text;
using BTCPayServer.Payouts;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public static class PullPaymentsExtensions
    {

        public static PullPaymentBlob GetBlob(this PullPaymentData data)
        {
            return JsonConvert.DeserializeObject<PullPaymentBlob>(data.Blob);
        }
        public static void SetBlob(this PullPaymentData data, PullPaymentBlob blob)
        {
            data.Blob = JsonConvert.SerializeObject(blob);
        }

        public static bool IsSupported(this PullPaymentData data, PayoutMethodId payoutMethodId)
        {
            return data.GetBlob().SupportedPayoutMethods.Contains(payoutMethodId);
        }
    }
}
