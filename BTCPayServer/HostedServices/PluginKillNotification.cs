using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins;
using BTCPayServer.Services.Notifications;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.HostedServices;

internal class PluginKillNotification : BaseNotification
{
    private const string TYPE = "pluginkill";

    internal class Handler(LinkGenerator linkGenerator, BTCPayServerOptions options)
        : NotificationHandler<PluginKillNotification>
    {
        public override string NotificationType => TYPE;

        public override (string identifier, string name)[] Meta
        {
            get
            {
                return [(TYPE, "Plugin update")];
            }
        }

        protected override void FillViewModel(PluginKillNotification notification, NotificationViewModel vm)
        {
            vm.Identifier = notification.Identifier;
            vm.Type = notification.NotificationType;
            vm.Body =
                $"The plugin {notification.Name} has been disabled through the vulnerability killswitch. Restart the server to apply the changes.";
            vm.ActionLink = linkGenerator.GetPathByAction(nameof(UIServerController.Maintenance),
                "UIServer",
                new {command = "soft-restart"}, options.RootPath);
            vm.ActionText = "Restart now";
        }
    }

    public PluginKillNotification()
    {
    }

    public PluginKillNotification(PluginService.AvailablePlugin plugin)
    {
        Name = plugin.Name;
        PluginIdentifier = plugin.Identifier;
        Version = plugin.Version.ToString();
    }

    public string PluginIdentifier { get; set; }

    public string Name { get; set; }

    public string Version { get; set; }
    public override string Identifier => TYPE;
    public override string NotificationType => TYPE;
}
