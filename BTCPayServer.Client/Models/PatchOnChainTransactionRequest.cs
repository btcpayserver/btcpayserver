#nullable enable
using System.Collections.Generic;

namespace BTCPayServer.Client.Models
{
    public class PatchOnChainTransactionRequest: PatchLabelsRequest
    {

        public string? Comment { get; set; } = null;
    }

    public class PatchLabelsRequest
    {
        public List<string>? Labels { get; set; } = null;
        public List<string>? RemoveLabels { get; set; } = null;
    }
}
