using System;
using System.Collections.Generic;
using System.Text;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class StoreReportRequest
{
    public string ViewName { get; set; }
    public TimePeriod TimePeriod { get; set; }
}
public class StoreReportResponse
{
    public class Field
    {
        public Field()
        {

        }
        public Field(string name, string type)
        {
            Name = name;
            Type = type;
        }
        public string Name { get; set; }
        public string Type { get; set; }
    }
    public IList<Field> Fields { get; set; } = new List<Field>();
    public List<JArray> Data { get; set; }
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}
public class TimePeriod
{
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? From { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? To { get; set; }
}
