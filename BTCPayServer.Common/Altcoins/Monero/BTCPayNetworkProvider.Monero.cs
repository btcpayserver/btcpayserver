using NBitcoin;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitMonero(NetworkType networkType)
        {
            return new MoneroLikeSpecificBtcPayNetwork()
            {
                CryptoCode = "XMR",
                DisplayName = "Monero",
                Divisibility = 12,
                BlockExplorerLink =
                    networkType == NetworkType.Mainnet
                        ? "https://www.exploremonero.com/transaction/{0}"
                        : "https://testnet.xmrchain.net/tx/{0}",
                DefaultRateRules = new[]
                {
                    "XMR_X = XMR_BTC * BTC_X",
                    "XMR_BTC = kraken(XMR_BTC)"
                },
                CryptoImagePath = "/imlegacy/monero.svg"
            };
        }
    }
}
