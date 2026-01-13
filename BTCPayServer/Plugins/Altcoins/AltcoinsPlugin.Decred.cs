using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins.Altcoins;

public partial class AltcoinsPlugin
{
    public void InitDecred(IServiceCollection services)
    {
        var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("DCR");
        var network = new BTCPayNetwork()
        {
            CryptoCode = nbxplorerNetwork.CryptoCode,
            DisplayName = "Decred",
            NBXplorerNetwork = nbxplorerNetwork,
            DefaultRateRules = new[]
            {
                "DCR_X = DCR_BTC * BTC_X",
                "DCR_BTC = poloniex(DCR_BTC)"
            },
            CryptoImagePath = "imlegacy/decred.svg",
            DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(ChainName),
            CoinType = ChainName == ChainName.Mainnet ? new KeyPath("42'") : new KeyPath("1'")
        }.SetDefaultElectrumMapping(ChainName);

        var blockExplorerLink = ChainName == ChainName.Mainnet ? "https://dcrdata.decred.org/tx/{0}" : "https://testnet.decred.org/tx/{0}";
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(PaymentTypes.CHAIN.GetPaymentMethodId(nbxplorerNetwork.CryptoCode), new DefaultTransactionLinkProvider(blockExplorerLink));
    }
}

