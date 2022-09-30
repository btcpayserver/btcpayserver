#nullable enable
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Labels
{
    public class TransactionMetadata
    {
        public TransactionMetadata(WalletObjectId id, JObject? value)
        {
            Id = id;
            Value = value;
        }
        public WalletObjectId Id { get; }
        public JObject? Value { get; }
    }
}
