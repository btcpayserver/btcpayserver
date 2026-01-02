#nullable enable
namespace BTCPayServer.Client.Models
{
    public class CreateOnChainTransactionResponse
    {
        public string PSBT { get; set; } = string.Empty;
        public string? Transaction { get; set; }
    }
}
