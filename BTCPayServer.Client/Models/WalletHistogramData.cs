using System;
using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models;

public enum HistogramType
{
    Week,
    Month,
    Year
}

public class HistogramData
{
    [JsonConverter(typeof(StringEnumConverter))]
    public HistogramType Type { get; set; }
    public List<decimal> Series { get; set; }
    public List<DateTimeOffset> Labels { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Balance { get; set; }
}
