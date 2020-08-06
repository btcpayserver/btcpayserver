using System;
using NBitcoin;
using NBitcoin.Payment;

namespace BTCPayServer.Data
{
    public class UriClaimDestination : IBitcoinLikeClaimDestination
    {
        private readonly BitcoinUrlBuilder _bitcoinUrl;

        public UriClaimDestination(BitcoinUrlBuilder bitcoinUrl)
        {
            if (bitcoinUrl == null)
                throw new ArgumentNullException(nameof(bitcoinUrl));
            if (bitcoinUrl.Address is null)
                throw new ArgumentException(nameof(bitcoinUrl));
            _bitcoinUrl = bitcoinUrl;
        }
        public BitcoinUrlBuilder BitcoinUrl => _bitcoinUrl;

        public BitcoinAddress Address => _bitcoinUrl.Address;
        public override string ToString()
        {
            return _bitcoinUrl.ToString();
        }
    }
}
