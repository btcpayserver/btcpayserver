using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Services;
using System.Net.Http;
using System.Net;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Altcoins.Monero.Configuration;
using BTCPayServer.Services.Altcoins.Monero.Payments;
using BTCPayServer.Services.Altcoins.Monero.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using BTCPayServer.Configuration;
using System.Linq;
using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BTCPayServer.Plugins.Altcoins;

public partial class AltcoinsPlugin
{
    public void InitMonero(IServiceCollection services)
    {
        var network = new MoneroLikeSpecificBtcPayNetwork()
        {
            CryptoCode = "XMR",
            DisplayName = "Monero",
            Divisibility = 12,
            DefaultRateRules = new[]
            {
                    "XMR_X = XMR_BTC * BTC_X",
                    "XMR_BTC = kraken(XMR_BTC)"
                },
            CryptoImagePath = "/imlegacy/monero.svg",
            UriScheme = "monero"
        };
        var blockExplorerLink = ChainName == ChainName.Mainnet
                    ? "https://www.exploremonero.com/transaction/{0}"
                    : "https://testnet.xmrchain.net/tx/{0}";
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("XMR");
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(pmi, new SimpleTransactionLinkProvider(blockExplorerLink));


        services.AddSingleton(provider =>
                ConfigureMoneroLikeConfiguration(provider));
        services.AddHttpClient("XMRclient")
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var configuration = provider.GetRequiredService<MoneroLikeConfiguration>();
                if (!configuration.MoneroLikeConfigurationItems.TryGetValue("XMR", out var xmrConfig) || xmrConfig.Username is null || xmrConfig.Password is null)
                {
                    return new HttpClientHandler();
                }
                return new HttpClientHandler
                {
                    Credentials = new NetworkCredential(xmrConfig.Username, xmrConfig.Password),
                    PreAuthenticate = true
                };
            });
        services.AddSingleton<MoneroRPCProvider>();
        services.AddHostedService<MoneroLikeSummaryUpdaterHostedService>();
        services.AddHostedService<MoneroListener>();
        services.AddSingleton<IPaymentMethodHandler>(provider =>
                (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(MoneroLikePaymentMethodHandler), new object[] { network }));
        services.AddSingleton<IPaymentLinkExtension>(provider =>
(IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(MoneroPaymentLinkExtension), new object[] { network, pmi }));
        services.AddSingleton<ICheckoutModelExtension>(provider =>
(ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(MoneroCheckoutModelExtension), new object[] { network, pmi }));

        services.AddUIExtension("store-nav", "Monero/StoreNavMoneroExtension");
        services.AddUIExtension("store-wallets-nav", "Monero/StoreWalletsNavMoneroExtension");
        services.AddUIExtension("store-invoices-payments", "Monero/ViewMoneroLikePaymentData");
        services.AddSingleton<ISyncSummaryProvider, MoneroSyncSummaryProvider>();
    }
    private static MoneroLikeConfiguration ConfigureMoneroLikeConfiguration(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
        var result = new MoneroLikeConfiguration();

        var supportedNetworks = btcPayNetworkProvider.GetAll()
            .OfType<MoneroLikeSpecificBtcPayNetwork>();

        foreach (var moneroLikeSpecificBtcPayNetwork in supportedNetworks)
        {
            var daemonUri =
                configuration.GetOrDefault<Uri>($"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_uri",
                    null);
            var walletDaemonUri =
                configuration.GetOrDefault<Uri>(
                    $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_uri", null);
            var walletDaemonWalletDirectory =
                configuration.GetOrDefault<string>(
                    $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_wallet_daemon_walletdir", null);
            var daemonUsername =
                configuration.GetOrDefault<string>(
                    $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_username", null);
            var daemonPassword =
                configuration.GetOrDefault<string>(
                    $"{moneroLikeSpecificBtcPayNetwork.CryptoCode}_daemon_password", null);
            if (daemonUri == null || walletDaemonUri == null || walletDaemonWalletDirectory == null)
            {
                throw new ConfigException($"{moneroLikeSpecificBtcPayNetwork.CryptoCode} is misconfigured");
            }

            result.MoneroLikeConfigurationItems.Add(moneroLikeSpecificBtcPayNetwork.CryptoCode, new MoneroLikeConfigurationItem()
            {
                DaemonRpcUri = daemonUri,
                Username = daemonUsername,
                Password = daemonPassword,
                InternalWalletRpcUri = walletDaemonUri,
                WalletDirectory = walletDaemonWalletDirectory
            });
        }
        return result;
    }
}

