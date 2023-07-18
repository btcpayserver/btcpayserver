#nullable enable
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class CreatePayoutThroughStoreRequest : CreatePayoutRequest
{
    public string? PullPaymentId { get; set; }
    public bool? Approved { get; set; }
    public JObject? Metadata { get; set; }
}
