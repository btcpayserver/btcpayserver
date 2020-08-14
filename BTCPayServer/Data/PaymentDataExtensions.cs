using BTCPayServer.Services.Invoices;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class PaymentDataExtensions
    {
        public static PaymentEntity GetBlob(this Data.PaymentData paymentData, BTCPayNetworkProvider networks)
        {
            var unziped = ZipUtils.Unzip(paymentData.Blob);
            var cryptoCode = "BTC";
            if (JObject.Parse(unziped).TryGetValue("cryptoCode", out var v) && v.Type == JTokenType.String)
                cryptoCode = v.Value<string>();
            var network = networks.GetNetwork<BTCPayNetworkBase>(cryptoCode);
            PaymentEntity paymentEntity = null;
            if (network == null)
            {
                return null;
            }
            else
            {
                paymentEntity = network.ToObject<PaymentEntity>(unziped);
            }
            paymentEntity.Network = network;
            paymentEntity.Accounted = paymentData.Accounted;
            return paymentEntity;
        }
    }
}
