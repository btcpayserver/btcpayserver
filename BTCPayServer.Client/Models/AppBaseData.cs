using System;
using Newtonsoft.Json;

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
}

public interface IAppRequest
{
    public string AppName { get; set; }
}
