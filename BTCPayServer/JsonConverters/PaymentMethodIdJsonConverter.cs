using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
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
            throw new JsonObjectException("Invalid payment method id", reader);
        }
        public override void WriteJson(JsonWriter writer, PaymentMethodId value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(value.ToString());
        }
    }
}
