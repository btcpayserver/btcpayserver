#nullable enable
using NBitcoin;

namespace BTCPayServer.Data
{
    public interface IClaimDestination
    {
        decimal? Amount { get; }
    }
}
