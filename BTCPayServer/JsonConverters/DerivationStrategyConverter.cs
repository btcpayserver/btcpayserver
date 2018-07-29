using System;
using System.Reflection;
using BTCPayServer.Payments;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.JsonConverters
{
    public class DerivationStrategyConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var derivationStrategy = (DerivationStrategy)value;
            var derivationStrategyData =  new DerivationStrategyData()
            {
                DerivationStrategy = derivationStrategy.ToString(),
                Enabled =  derivationStrategy.Enabled
            };
            serializer.Serialize(writer, derivationStrategyData);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new InvalidOperationException("Cannot deserialize DerivationStrategy manually: Deserialize to type DerivationStrategyData");
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(DerivationStrategy).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }
    }

    //public class PaymentMethodConverter : JsonConverter
    //{
    //    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    //    {
    //        var paymentMethod = (ISupportedPaymentMethod)value;
    //        switch (paymentMethod.PaymentId.PaymentType)
    //        {
    //            case PaymentTypes.BTCLike:
    //                writer.WriteRaw(JObject.FromObject((DerivationStrategy)value).ToString());
    //                break;
    //            case PaymentTypes.LightningLike:
    //                writer.WriteRaw(JObject.FromObject((DerivationStrategy)value).ToString());
    //                break;
    //        }
    //        var derivationStrategyData = new DerivationStrategyData()
    //        {
    //            DerivationStrategy = derivationStrategy.ToString(),
    //            Enabled = derivationStrategy.Enabled
    //        };
    //        serializer.Serialize(writer, derivationStrategyData);
    //    }

    //    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public override bool CanConvert(Type objectType)
    //    {
    //        return typeof(ISupportedPaymentMethod).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
    //    }
    //}
}
