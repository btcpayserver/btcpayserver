using System;
using System.Net.Http;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.GlobalSearch;
using BTCPayServer.Plugins.PluginManagement.Controllers;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.PluginManagement;

public class PluginManagerPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "PluginManager";
    public override string Identifier => "BTCPayServer.Plugins.PluginManager";
    public override string Name => "Plugin Manager";
    public override string Description => "Manage BTCPay Server plugins.";

    public override void Execute(IServiceCollection services)
    {
        services.AddHttpClient<PluginBuilderClient>((provider, httpClient) =>
            ConfigurePluginBuilderClient(provider.GetRequiredService<PoliciesSettings>(), httpClient));
        services.AddTransient<PluginService>();
        services.AddScheduledTask<PluginUpdateFetcher>(TimeSpan.FromDays(1));
        services.AddSingleton<INotificationHandler, PluginUpdateNotification.Handler>();
        services.AddStaticSearch([
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "View installed plugins",
                Action = nameof(UIPluginManagerController.ListPlugins),
                Controller = "UIPluginManager",
                Values = _ => new { area = Area },
                Category = "Server",
                Keywords = ["Plugins", "Installed", "Update", "Configure"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "Browse the plugin directory",
                Action = nameof(UIPluginManagerController.PluginDirectory),
                Controller = "UIPluginManager",
                Values = _ => new { area = Area },
                Category = "Server",
                Keywords = ["Plugins", "Directory", "Install"]
            }
        ]);
    }

    internal static void ConfigurePluginBuilderClient(PoliciesSettings policiesSettings, HttpClient httpClient)
    {
        var pluginSource = policiesSettings.PluginSource ?? PoliciesSettings.DefaultPluginSource;
        if (!Uri.TryCreate(pluginSource, UriKind.Absolute, out var baseAddress) ||
            (baseAddress.Scheme != Uri.UriSchemeHttps && baseAddress.Scheme != Uri.UriSchemeHttp))
        {
            baseAddress = new Uri(PoliciesSettings.DefaultPluginSource, UriKind.Absolute);
        }

        httpClient.BaseAddress = PluginBuilderClient.NormalizeBaseAddress(baseAddress);
    }
}
