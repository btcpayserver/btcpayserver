using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NBitcoin;
using Newtonsoft.Json;

public class WordcountJsonConverter : JsonConverter
{
    static WordcountJsonConverter()
    {
        _Wordcount = new Dictionary<long, WordCount>()
        {
            {18, WordCount.Eighteen},
            {15, WordCount.Fifteen},
            {12, WordCount.Twelve},
            {24, WordCount.TwentyFour},
            {21, WordCount.TwentyOne}
        };
        _WordcountReverse = _Wordcount.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    public override bool CanConvert(Type objectType)
    {
        return typeof(NBitcoin.WordCount).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo()) ||
               typeof(NBitcoin.WordCount?).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return default;
        if (reader.TokenType != JsonToken.Integer)
            throw new NBitcoin.JsonConverters.JsonObjectException(
                $"Unexpected json token type, expected Integer, actual {reader.TokenType}", reader);
        if (!_Wordcount.TryGetValue((long)reader.Value, out var result))
            throw new NBitcoin.JsonConverters.JsonObjectException(
                $"Invalid WordCount, possible values {string.Join(", ", _Wordcount.Keys.ToArray())} (default: 12)",
                reader);
        return result;
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is WordCount wc)
            writer.WriteValue(_WordcountReverse[wc]);
    }

    readonly static Dictionary<long, WordCount> _Wordcount = new Dictionary<long, WordCount>()
    {
        {18, WordCount.Eighteen},
        {15, WordCount.Fifteen},
        {12, WordCount.Twelve},
        {24, WordCount.TwentyFour},
        {21, WordCount.TwentyOne}
    };

    readonly static Dictionary<WordCount, long> _WordcountReverse;
}
