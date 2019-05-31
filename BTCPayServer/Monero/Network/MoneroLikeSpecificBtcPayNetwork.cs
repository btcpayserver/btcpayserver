namespace BTCPayServer
{
    public class MoneroLikeSpecificBtcPayNetwork : BTCPayNetwork
    {

    }
    
//    public partial class BTCPayNetworkProvider
//    {
//        public void InitBitcoin()
//        {
//            Add(new MoneroLikeSpecificBtcPayNetwork()
//            {
//                CryptoCode = "XMR",
//                DisplayName = "Monero",
//                BlockExplorerLink =
//                    NetworkType == NetworkType.Mainnet
//                        ? "https://www.exploremonero.com/transaction/{0}"
//                        : "https://testnet.xmrchain.net/tx/{0}",
//                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
//                NBXplorerNetwork = nbxplorerNetwork,
//                UriScheme = "bitcoin",
//                CryptoImagePath = "imlegacy/bitcoin.svg",
//                LightningImagePath = "imlegacy/bitcoin-lightning.svg",
//                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings()
//                CPayDefaultSettings.GetDefaultSettings(NetworkType)
//            });
//    }
}
