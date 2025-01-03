using System;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class BearerTokenData
{
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset Expiry { get; set; }
}
