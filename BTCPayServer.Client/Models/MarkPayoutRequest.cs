#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class MarkPayoutRequest
{
    [JsonConverter(typeof(StringEnumConverter))]
    public PayoutState State { get; set; } = PayoutState.Completed;

    public JObject? PaymentProof { get; set; }
}
