using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitDash()
        {
            //not needed: NBitcoin.Altcoins.Dash.Instance.EnsureRegistered();
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("DASH");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Dash",
                BlockExplorerLink = NetworkType == ChainName.Mainnet
                    ? "https://insight.dash.org/insight/tx/{0}"
                    : "https://testnet-insight.dashevo.org/insight/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "dash",
                DefaultRateRules = new[]
                    {
                        "DASH_X = DASH_BTC * BTC_X",
                        "DASH_BTC = bitfinex(DSH_BTC)"
                    },
                CryptoImagePath = "imlegacy/dash.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                //https://github.com/satoshilabs/slips/blob/master/slip-0044.md
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("5'")
                    : new KeyPath("1'")
            });
        }
    }
}
