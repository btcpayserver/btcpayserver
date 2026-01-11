using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;

namespace BTCPayServer.Plugins.Altcoins;

public partial class AltcoinsPlugin
{
    public void InitGroestlcoin(IServiceCollection services)
    {
        var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("GRS");
        var network = new BTCPayNetwork()
        {
            CryptoCode = nbxplorerNetwork.CryptoCode,
            DisplayName = "Groestlcoin",
            NBXplorerNetwork = nbxplorerNetwork,
            DefaultRateRules = new[]
            {
                    "GRS_X = GRS_BTC * BTC_X",
                    "GRS_BTC = upbit(GRS_BTC)"
                },
            CryptoImagePath = "imlegacy/groestlcoin.png",
            LightningImagePath = "imlegacy/groestlcoin-lightning.svg",
            DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(ChainName),
            CoinType = ChainName == ChainName.Mainnet ? new KeyPath("17'") : new KeyPath("1'"),
            SupportRBF = true,
            SupportPayJoin = true,
            VaultSupported = true
        }.SetDefaultElectrumMapping(ChainName);
        var blockExplorerLink = ChainName == ChainName.Mainnet
                ? "https://chainz.cryptoid.info/grs/tx.dws?{0}.htm"
                : "https://chainz.cryptoid.info/grs-test/tx.dws?{0}.htm";
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(PaymentTypes.CHAIN.GetPaymentMethodId(nbxplorerNetwork.CryptoCode), new DefaultTransactionLinkProvider(blockExplorerLink));
    }
}

