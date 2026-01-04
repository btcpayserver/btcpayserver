using NBitcoin;

namespace BTCPayServer.Data
{
    public interface IBitcoinLikeClaimDestination : IClaimDestination
    {
        BitcoinAddress Address { get; }
    }
}
