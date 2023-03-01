using System.Runtime.InteropServices;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public static class PaymentDataExtensions
    {
        public static void SetBlob(this PaymentData paymentData, PaymentEntity entity)
        {
            paymentData.Type = entity.GetPaymentMethodId().ToStringNormalized();
            paymentData.Blob2 = entity.Network.ToString(entity);
        }
        public static PaymentEntity GetBlob(this PaymentData paymentData, BTCPayNetworkProvider networks)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (paymentData.Blob is not null && paymentData.Blob.Length != 0)
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
#pragma warning restore CS0618 // Type or member is obsolete
            if (paymentData.Blob2 is not null)
            {
                if (!PaymentMethodId.TryParse(paymentData.Type, out var pmi))
                    return null;
                var network = networks.GetNetwork<BTCPayNetworkBase>(pmi.CryptoCode);
                if (network is null)
                    return null;
                var entity = network.ToObject<PaymentEntity>(paymentData.Blob2);
                entity.Network = network;
                entity.Accounted = paymentData.Accounted;
                return entity;
            }
            return null;
        }
    }
}
