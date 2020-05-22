using System;
using System.Reflection;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;
using NBitcoin.JsonConverters;

namespace BTCPayServer.JsonConverters
{
    public class CurrencyValueJsonConverter : JsonConverter
    {
        private readonly CurrencyNameTable _currencyNameTable;
        public CurrencyValueJsonConverter()
        {
            _currencyNameTable = new CurrencyNameTable();
        }
        public CurrencyValueJsonConverter(CurrencyNameTable currencyNameTable)
        {
            _currencyNameTable = currencyNameTable?? new CurrencyNameTable();
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(CurrencyValue).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                return reader.TokenType == JsonToken.Null ? null :
                       CurrencyValue.TryParse((string)reader.Value, out var result, _currencyNameTable) ? result :
                       throw new JsonObjectException("Invalid Currency value", reader);
            }
            catch (InvalidCastException)
            {
                throw new JsonObjectException("Invalid Currency value", reader);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if(value != null)
                writer.WriteValue(value.ToString());
        }
    }
}
