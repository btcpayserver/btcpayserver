using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using NBitcoin;
using NBitpayClient;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitBitcoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("BTC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Bitcoin",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://www.smartbit.com.au/tx/{0}" : "https://testnet.smartbit.com.au/tx/{0}",
                NBitcoinNetwork = nbxplorerNetwork.NBitcoinNetwork,
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcoin",
                CryptoImagePath = "imlegacy/bitcoin.svg",
                LightningImagePath = "imlegacy/bitcoin-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("0'") : new KeyPath("1'"),
                SupportRBF = true,
                ElectrumMapping = new Dictionary<uint, string[]>()
                {
                    //https://github.com/spesmilo/electrum/blob/11733d6bc271646a00b69ff07657119598874da4/electrum/constants.py
                    //mainnet
                    {0x0488b21eU, new[] { "legacy" }},
                    {0x049d7cb2U, new[] { "p2sh" }},
                    {0x4b24746U, Array.Empty<string>()},
                    //testnet
                    {0x043587cfU, new[] { "legacy" }},
                    {0x044a5262U, new[] { "p2sh" }},
                    {0x045f1cf6U, Array.Empty<string>()}
                }
            });
        }
    }
}
