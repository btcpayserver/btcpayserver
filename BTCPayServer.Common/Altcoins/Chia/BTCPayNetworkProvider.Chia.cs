using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitChia()
        {
            Add(new ChiaLikeSpecificBtcPayNetwork()
            {
                CryptoCode = "XCH",
                DisplayName = "Chia",
                Divisibility = 12,
                BlockExplorerLink = "https://www.spacescan.io/coin/{0}",
                DefaultRateRules = new[]
                {
                    "XCH_X = XCH_USD * USD_X",
                    "XCH_USD = 23"
                },
                CryptoImagePath = "/imlegacy/chia.png",
                UriScheme = "chia"
            });
        }
    }
}
