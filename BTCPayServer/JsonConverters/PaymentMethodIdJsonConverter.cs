using System;
using BTCPayServer.Payments;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.JsonConverters
{
    public class PaymentMethodIdJsonConverter : JsonConverter<PaymentMethodId>
    {
        public override PaymentMethodId ReadJson(JsonReader reader, Type objectType, PaymentMethodId existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            if (reader.TokenType != JsonToken.String)
                throw new JsonObjectException("A payment method id should be a string", reader);
            if (PaymentMethodId.TryParse((string)reader.Value, out var result))
                return result;
            return null;
            // We need to do this gracefully as we have removed support for a payment type in the past which will throw here on your store each time it is loaded.
            // throw new JsonObjectException($"Invalid payment method id ({(string)reader.Value})", reader);
        }
        public override void WriteJson(JsonWriter writer, PaymentMethodId value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(value.ToString());
        }
    }
}
