#nullable enable
using System.Collections.Generic;

namespace BTCPayServer.Client.Models
{
    public class PatchOnChainTransactionRequest
    {

        public string? Comment { get; set; } = null;
        public List<string>? Labels { get; set; } = null;
    }
}
