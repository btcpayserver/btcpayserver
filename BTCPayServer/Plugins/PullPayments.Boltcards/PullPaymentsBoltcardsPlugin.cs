using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.PullPayments.Boltcards;

public class PullPaymentsBoltcardsPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "PullPayments.Boltcards";
    public override string Identifier => "BTCPayServer.Plugins.PullPayments.Boltcards";
    public override string Name => "Boltcards";
    public override string Description => "Create boltcards from pull payments";


    public override void Execute(IServiceCollection applicationBuilder)
    {
        applicationBuilder.AddUIExtension("pullpayment-store-footer", "/Plugins/PullPayments.Boltcards/Views/BoltcardPullPaymentExtension.cshtml");
    }
}
