using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitBitcoinGold(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("BTG");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "BGold",
                BlockExplorerLink = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? "https://explorer.bitcoingold.org/insight/tx/{0}/" : "https://test-explorer.bitcoingold.org/insight/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcoingold",
                DefaultRateRules = new[]
                {
                    "BTG_X = BTG_BTC * BTC_X",
                    "BTG_BTC = bitfinex(BTG_BTC)",
                },
                CryptoImagePath = "imlegacy/btg.svg",
                LightningImagePath = "imlegacy/btg-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? new KeyPath("156'") : new KeyPath("1'")
            };
        }
    }
}
