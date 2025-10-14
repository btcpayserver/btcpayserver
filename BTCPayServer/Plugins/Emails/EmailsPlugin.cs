using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Webhooks;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.Emails;

public class EmailsPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Emails";
    public override string Identifier => "BTCPayServer.Plugins.Emails";
    public override string Name => "Emails";
    public override string Description => "Allows you to send emails to your customers!";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<IDefaultTranslationProvider, EmailsTranslationProvider>();
        services.AddSingleton<IHostedService, StoreEmailRuleProcessorSender>();
    }
}
