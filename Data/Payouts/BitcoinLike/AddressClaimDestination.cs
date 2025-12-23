using System;
using NBitcoin;

namespace BTCPayServer.Data
{
    public class AddressClaimDestination : IBitcoinLikeClaimDestination
    {
        public BitcoinAddress _bitcoinAddress;

        public AddressClaimDestination(BitcoinAddress bitcoinAddress)
        {
            ArgumentNullException.ThrowIfNull(bitcoinAddress);
            _bitcoinAddress = bitcoinAddress;
        }
        public BitcoinAddress Address => _bitcoinAddress;
        public override string ToString()
        {
            return _bitcoinAddress.ToString();
        }

        public string Id => ToString();
        public decimal? Amount => null;
    }
}
