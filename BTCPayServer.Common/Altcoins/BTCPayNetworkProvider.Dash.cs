using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitDash(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            //not needed: NBitcoin.Altcoins.Dash.Instance.EnsureRegistered();
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("DASH");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Dash",
                BlockExplorerLink = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet
                    ? "https://insight.dash.org/insight/tx/{0}"
                    : "https://testnet-insight.dashevo.org/insight/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "dash",
                DefaultRateRules = new[]
                    {
                        "DASH_X = DASH_BTC * BTC_X",
                        "DASH_BTC = bittrex(DASH_BTC)"
                    },
                CryptoImagePath = "imlegacy/dash.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                //https://github.com/satoshilabs/slips/blob/master/slip-0044.md
                CoinType = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? new KeyPath("5'")
                    : new KeyPath("1'")
            };
        }
    }
}
