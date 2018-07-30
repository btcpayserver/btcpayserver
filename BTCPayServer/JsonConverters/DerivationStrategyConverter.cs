using System;
using System.Reflection;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
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
}
