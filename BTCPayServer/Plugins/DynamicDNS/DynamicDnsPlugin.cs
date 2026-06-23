using BTCPayServer.Abstractions.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Plugins.DynamicDNS.Controllers;
using BTCPayServer.Plugins.GlobalSearch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.DynamicDNS;

public class DynamicDnsPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "DynamicDNS";
    public override string Identifier => "BTCPayServer.Plugins.DynamicDNS";
    public override string Name => "Dynamic DNS";
    public override string Description => "Allows BTCPay Server to refresh Dynamic DNS records";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<IHostedService, DynamicDnsHostedService>();
        services.AddSingleton(new ServicesViewModel.OtherExternalService()
        {
            Name = "Dynamic DNS",
            ControllerName = "UIDynamicDns",
            ActionName = nameof(UIDynamicDnsController.DynamicDnsService),
            RouteValues = new { area = Area }
        });
        services.AddStaticSearch(new ActionResultItemViewModel()
        {
            Title = "Setup Dynamic DNS",
            Keywords = new[] { "Dynamic DNS", "DDNS", "DynDns", "DNS" },
            Action = nameof(UIDynamicDnsController.DynamicDnsService),
            Controller = "UIDynamicDns",
            Values = ctx => new { area = Area },
            Category = "Server",
            RequiredPolicy = Client.Policies.CanModifyServerSettings
        });
    }
}
