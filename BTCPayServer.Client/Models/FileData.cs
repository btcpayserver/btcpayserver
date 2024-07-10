using System;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class FileData
{
    public string Id { get; set; }
    public string UserId { get; set; }
    public string Uri { get; set; }
    public string Url { get; set; }
    public string OriginalName { get; set; }
    public string StorageName { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? CreatedAt { get; set; }
}
