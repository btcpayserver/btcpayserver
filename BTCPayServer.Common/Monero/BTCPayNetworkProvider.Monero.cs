using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitMonero()
        {
            Add(new MoneroLikeSpecificBtcPayNetwork()
            {
                CryptoCode = "XMR",
                DisplayName = "Monero",
                BlockExplorerLink =
                    NetworkType == NetworkType.Mainnet
                        ? "https://www.exploremonero.com/transaction/{0}"
                        : "https://testnet.xmrchain.net/tx/{0}",
                CryptoImagePath = "/imlegacy/monero.svg"
            });
        }
    }
}
