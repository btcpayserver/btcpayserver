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
                CryptoCode = "YEC",
                DisplayName = "Ycash",
                Divisibility = 8,
                BlockExplorerLink =
                    NetworkType == ChainName.Mainnet
                        ? "https://www.exploreZcash.com/transaction/{0}"
                        : "https://testnet.xmrchain.net/tx/{0}",
                DefaultRateRules = new[]
                {
                    "YEC_X = YEC_BTC * BTC_X",
                    "YEC_BTC = kraken(YEC_BTC)"
                },
                CryptoImagePath = "/imlegacy/ycash.png",
                UriScheme = "ycash"
            });
        }
    }
}
