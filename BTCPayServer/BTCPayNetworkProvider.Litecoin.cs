using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitLitecoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("LTC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Litecoin",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://live.blockcypher.com/ltc/tx/{0}/" : "http://explorer.litecointools.com/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "litecoin",
                CryptoImagePath = "imlegacy/litecoin.svg",
                LightningImagePath = "imlegacy/litecoin-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("2'") : new KeyPath("1'"),
                ElectrumMapping = new Dictionary<uint, string[]>()
                {
                    //https://github.com/pooler/electrum-ltc/blob/0d6989a9d2fb2edbea421c116e49d1015c7c5a91/electrum_ltc/constants.py
                    //mainnet
                    {0x0488b21eU, new[] { "legacy" }},
                    {0x049d7cb2U, new[] { "p2sh" }},
                    {0x04b24746U, Array.Empty<string>()},
                    //testnet
                    {0x043587cfU, new[] { "legacy" }},
                    {0x044a5262U, new[] { "p2sh" }},
                    {0x045f1cf6U, Array.Empty<string>()}
                }
            });
        }
    }
}
