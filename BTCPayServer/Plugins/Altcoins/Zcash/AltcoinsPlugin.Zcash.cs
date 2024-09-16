#nullable enable
using BTCPayServer.Services;
using System.Globalization;
using System.Linq;
using NBitcoin;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Services.Altcoins.Zcash.Payments;
using BTCPayServer.Services.Altcoins.Zcash.Services;
using BTCPayServer.Configuration;
using BTCPayServer.Services.Altcoins.Zcash.Configuration;
using System;
using Microsoft.Extensions.Configuration;

namespace BTCPayServer.Plugins.Altcoins;

public partial class AltcoinsPlugin
{
    // Change this if you want another zcash coin
    public void InitZcash(IServiceCollection services)
    {
        var network = new ZcashLikeSpecificBtcPayNetwork()
        {
            CryptoCode = "ZEC",
            DisplayName = "Zcash",
            Divisibility = 8,
            DefaultRateRules = new[]
            {
                    "ZEC_X = ZEC_BTC * BTC_X",
                    "ZEC_BTC = kraken(ZEC_BTC)"
                },
            CryptoImagePath = "/imlegacy/zcash.png",
            UriScheme = "zcash"
        };
        var blockExplorerLink = ChainName == ChainName.Mainnet
                    ? "https://www.exploreZcash.com/transaction/{0}"
                    : "https://testnet.xmrchain.net/tx/{0}";
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("ZEC");
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(network.CryptoCode, new SimpleTransactionLinkProvider(blockExplorerLink));
        services.AddSingleton<IPaymentMethodViewExtension>(provider =>
(IPaymentMethodViewExtension)ActivatorUtilities.CreateInstance(provider, typeof(BitcoinPaymentMethodViewExtension), new object[] { pmi }));


        services.AddSingleton(provider =>
            ConfigureZcashLikeConfiguration(provider));
        services.AddSingleton<ZcashRPCProvider>();
        services.AddHostedService<ZcashLikeSummaryUpdaterHostedService>();
        services.AddHostedService<ZcashListener>();


        services.AddSingleton<IPaymentMethodHandler>(provider =>
        (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(ZcashLikePaymentMethodHandler), new object[] { network }));
        services.AddSingleton<IPaymentLinkExtension>(provider =>
(IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(ZcashPaymentLinkExtension), new object[] { network, pmi }));
        services.AddSingleton<IPaymentModelExtension>(provider =>
(IPaymentModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(ZcashPaymentModelExtension), new object[] { network, pmi }));

        services.AddSingleton<ZcashLikePaymentMethodHandler>();
        services.AddSingleton<IPaymentMethodHandler>(provider => provider.GetRequiredService<ZcashLikePaymentMethodHandler>());
        services.AddSingleton<IUIExtension>(new UIExtension("Zcash/StoreNavZcashExtension", "store-nav"));
        services.AddSingleton<IUIExtension>(new UIExtension("Zcash/ViewZcashLikePaymentData", "store-invoices-payments"));
        services.AddSingleton<ISyncSummaryProvider, ZcashSyncSummaryProvider>();

    }
    static ZcashLikeConfiguration ConfigureZcashLikeConfiguration(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        var btcPayNetworkProvider = serviceProvider.GetRequiredService<BTCPayNetworkProvider>();
        var result = new ZcashLikeConfiguration();

        var supportedNetworks = btcPayNetworkProvider.GetAll()
            .OfType<ZcashLikeSpecificBtcPayNetwork>();

        foreach (var ZcashLikeSpecificBtcPayNetwork in supportedNetworks)
        {
            var daemonUri =
                configuration.GetOrDefault<Uri?>($"{ZcashLikeSpecificBtcPayNetwork.CryptoCode}_daemon_uri",
                    null);
            var walletDaemonUri =
                configuration.GetOrDefault<Uri?>(
                    $"{ZcashLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_uri", null);
            var walletDaemonWalletDirectory =
                configuration.GetOrDefault<string?>(
                    $"{ZcashLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_walletdir", null);
            if (daemonUri == null || walletDaemonUri == null || walletDaemonWalletDirectory == null)
            {
                throw new ConfigException($"{ZcashLikeSpecificBtcPayNetwork.CryptoCode} is misconfigured");
            }

            result.ZcashLikeConfigurationItems.Add(ZcashLikeSpecificBtcPayNetwork.CryptoCode, new ZcashLikeConfigurationItem()
            {
                DaemonRpcUri = daemonUri,
                InternalWalletRpcUri = walletDaemonUri,
                WalletDirectory = walletDaemonWalletDirectory
            });
        }
        return result;
    }
    class SimpleTransactionLinkProvider : DefaultTransactionLinkProvider
    {
        public SimpleTransactionLinkProvider(string blockExplorerLink) : base(blockExplorerLink)
        {
        }

        public override string? GetTransactionLink(string paymentId)
        {
            if (string.IsNullOrEmpty(BlockExplorerLink))
                return null;
            return string.Format(CultureInfo.InvariantCulture, BlockExplorerLink, paymentId);
        }
    }
}

