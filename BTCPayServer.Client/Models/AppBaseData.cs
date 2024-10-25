using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class AppBaseData
{
    public string Id { get; set; }
    public string AppType { get; set; }
    public string AppName { get; set; }
    public string StoreId { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? Archived { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset Created { get; set; }
    [JsonExtensionData]
    public IDictionary<string, JToken> AdditionalData { get; set; } = new Dictionary<string, JToken>();
}

public interface IAppRequest
{
    public string AppName { get; set; }
}
