#nullable enable
using BTCPayServer.Services;
using System.Globalization;
using System.Linq;
using NBitcoin;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;

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
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(new Payments.PaymentMethodId("ZEC", PaymentTypes.BTCLike), new SimpleTransactionLinkProvider(blockExplorerLink));
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

