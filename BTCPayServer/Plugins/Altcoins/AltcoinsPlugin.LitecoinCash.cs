using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;

namespace BTCPayServer.Plugins.Altcoins;

public partial class AltcoinsPlugin
{
    public void InitLitecoinCash(IServiceCollection services)
    {
        var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("LCC");
        var network = new BTCPayNetwork()
        {
            CryptoCode = nbxplorerNetwork.CryptoCode,
            DisplayName = "Litecoin Cash",
            NBXplorerNetwork = nbxplorerNetwork,
            DefaultRateRules = new[]
            {
                "LCC_X = LCC_BTC * BTC_X",
                "LCC_BTC = coinpaprika(LCC_BTC)"
            },
            DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(ChainName),
            CoinType = ChainName == ChainName.Mainnet
                ? new KeyPath("192'")
                : new KeyPath("1'")
        }.SetDefaultElectrumMapping(ChainName);

        var blockExplorerLinks = ChainName == ChainName.Mainnet
            ? "https://chainz.cryptoid.info/lcc/tx.dws?{0}"
            : null;

        services.AddBTCPayNetwork(network);

        if (blockExplorerLinks is not null)
        {
            services.AddTransactionLinkProvider(
                PaymentTypes.CHAIN.GetPaymentMethodId(nbxplorerNetwork.CryptoCode),
                new DefaultTransactionLinkProvider(blockExplorerLinks));
        }
    }
}
