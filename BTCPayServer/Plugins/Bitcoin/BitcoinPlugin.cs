#nullable enable
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer.Plugins.Bitcoin
{
    public class BitcoinPlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.Bitcoin";
        public override string Name => "Bitcoin";
        public override string Description => "Add Bitcoin support";

        public override void Execute(IServiceCollection applicationBuilder)
        {
            var services = (PluginServiceCollection)applicationBuilder;
            var onChain = new Payments.PaymentMethodId("BTC", Payments.PaymentTypes.BTCLike);
            var nbxplorerNetworkProvider = services.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>();
            var nbxplorerNetwork = nbxplorerNetworkProvider.GetFromCryptoCode("BTC");
            var chainName = nbxplorerNetwork.NBitcoinNetwork.ChainName;
            var selectedChains = services.BootstrapServices.GetRequiredService<SelectedChains>();
            if (!services.BootstrapServices.GetRequiredService<SelectedChains>().Contains("BTC"))
                return;
            var blockExplorerLink = chainName == ChainName.Mainnet ? "https://mempool.space/tx/{0}" :
                chainName == NBitcoin.Bitcoin.Instance.Signet.ChainName ? "https://mempool.space/signet/tx/{0}"
                : "https://mempool.space/testnet/tx/{0}";
            
            var defaultTransactionLinkProvider = new DefaultTransactionLinkProvider(blockExplorerLink);
           
            var network = new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Bitcoin",
                NBXplorerNetwork = nbxplorerNetwork,
                CryptoImagePath = "imlegacy/bitcoin.svg",
                LightningImagePath = "imlegacy/bitcoin-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(chainName),
                CoinType = chainName == ChainName.Mainnet ? new KeyPath("0'") : new KeyPath("1'"),
                SupportRBF = true,
                SupportPayJoin = true,
                VaultSupported = true,
#pragma warning disable CS0618 // Type or member is obsolete
                BlockExplorerLink = defaultTransactionLinkProvider.BlockExplorerLink
#pragma warning restore CS0618 // Type or member is obsolete
            }.SetDefaultElectrumMapping(chainName);
           
            applicationBuilder.AddBTCPayNetwork(network);
            applicationBuilder.AddTransactionLinkProvider(onChain, defaultTransactionLinkProvider);
        }
    }
}
