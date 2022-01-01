using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        // Change this if you want another zcash coin
        public void InitZcash()
        {
            Add(new ZcashLikeSpecificBtcPayNetwork()
            {
                CryptoCode = "ZEC",
                DisplayName = "Zcash",
                Divisibility = 8,
                BlockExplorerLink =
                    NetworkType == ChainName.Mainnet
                        ? "https://www.exploreZcash.com/transaction/{0}"
                        : "https://testnet.xmrchain.net/tx/{0}",
                DefaultRateRules = new[]
                {
                    "ZEC_X = ZEC_BTC * BTC_X",
                    "ZEC_BTC = kraken(ZEC_BTC)"
                },
                CryptoImagePath = "/imlegacy/zcash.png",
                UriScheme = "zcash"
            });
        }
    }
}
