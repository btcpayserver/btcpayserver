using System;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class UpdateSubscriberDatesRequest
{
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? StartDate { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? ExpirationDate { get; set; }
}
