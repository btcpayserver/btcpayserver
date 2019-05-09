using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitGroestlcoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("GRS");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Groestlcoin",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet
                    ? "https://chainz.cryptoid.info/grs/tx.dws?{0}.htm"
                    : "https://chainz.cryptoid.info/grs-test/tx.dws?{0}.htm",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "groestlcoin",
                DefaultRateRules = new[]
                {
                    "GRS_X = GRS_BTC * BTC_X",
                    "GRS_BTC = bittrex(GRS_BTC)"
                },
                CryptoImagePath = "imlegacy/groestlcoin.png",
                LightningImagePath = "imlegacy/groestlcoin-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("17'") : new KeyPath("1'"),
                //https://github.com/Groestlcoin/electrum-grs/blob/6799baba60305164126a92b52e5e95284ed44543/electrum_grs/constants.py
                ElectrumMapping = NetworkType == NetworkType.Mainnet
                    ? new Dictionary<uint, string[]>()
                    {
                        {0x0488b21eU, new[] {"legacy"}},
                        {0x049d7cb2U, new[] {"p2sh"}},
                        {0x04b24746U, Array.Empty<string>()},
                    }
                    : new Dictionary<uint, string[]>()
                    {
                        {0x043587cfU, new[] {"legacy"}},
                        {0x044a5262U, new[] {"p2sh"}},
                        {0x045f1cf6U, Array.Empty<string>()}
                    }
            });
        }
    }
}
