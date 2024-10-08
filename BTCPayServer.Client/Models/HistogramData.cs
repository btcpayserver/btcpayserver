using System;
using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models;

public enum HistogramType
{
    Week,
    Month,
    YTD,
    Year,
    TwoYears,
    Day
}

public class HistogramData
{
    [JsonConverter(typeof(StringEnumConverter))]
    public HistogramType Type { get; set; }
    [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
    public List<decimal> Series { get; set; }
    [JsonProperty(ItemConverterType = typeof(DateTimeToUnixTimeConverter))]
    public List<DateTimeOffset> Labels { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Balance { get; set; }
}
