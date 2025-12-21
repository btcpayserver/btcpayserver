#nullable enable
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
            ArgumentNullException.ThrowIfNull(bitcoinUrl);
            if (bitcoinUrl.Address is null)
                throw new ArgumentException(nameof(bitcoinUrl));
            _bitcoinUrl = bitcoinUrl;
            Address = bitcoinUrl.Address ?? throw new FormatException("the bip21 doesn't contain an address");
        }
        public BitcoinUrlBuilder BitcoinUrl => _bitcoinUrl;
        public BitcoinAddress Address { get; }
        public override string ToString()
        {
            return _bitcoinUrl.ToString();
        }

        public string Id => Address.ToString();
        public decimal? Amount => _bitcoinUrl.Amount?.ToDecimal(MoneyUnit.BTC);
    }
}
