using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins.Altcoins;

public partial class AltcoinsPlugin
{
    public void InitMonacoin(IServiceCollection services)
    {
        var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("MONA");
        var network = new BTCPayNetwork
        {
            CryptoCode = nbxplorerNetwork.CryptoCode,
            DisplayName = "Monacoin",
            NBXplorerNetwork = nbxplorerNetwork,
            DefaultRateRules = new[]
            {
                                "MONA_X = MONA_BTC * BTC_X",
                                "MONA_JPY = bitbank(MONA_JPY)",
                                "MONA_BTC = MONA_JPY * JPY_BTC"
                },
            CryptoImagePath = "imlegacy/monacoin.png",
            LightningImagePath = "imlegacy/mona-lightning.svg",
            DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(ChainName),
            CoinType = ChainName == ChainName.Mainnet ? new KeyPath("22'") : new KeyPath("1'")
        }.SetDefaultElectrumMapping(ChainName);

        var blockExplorerLink = ChainName == ChainName.Mainnet ? "https://mona.insight.monaco-ex.org/insight/tx/{0}" : "https://testnet-mona.insight.monaco-ex.org/insight/tx/{0}";
        services.AddBTCPayNetwork(network)
            .AddTransactionLinkProvider(nbxplorerNetwork.CryptoCode, new DefaultTransactionLinkProvider(blockExplorerLink));
    }
}

