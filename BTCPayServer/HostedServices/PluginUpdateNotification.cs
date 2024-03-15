using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins;
using BTCPayServer.Services.Notifications;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.HostedServices;

internal class PluginUpdateNotification : BaseNotification
{
    public bool UpdateDownloaded { get; }
    private const string TYPE = "pluginupdate";

    internal class Handler(LinkGenerator linkGenerator, BTCPayServerOptions options) : NotificationHandler<PluginUpdateNotification>
    {
        public override string NotificationType => TYPE;

        public override (string identifier, string name)[] Meta
        {
            get
            {
                return [(TYPE, "Plugin update")];
            }
        }

        protected override void FillViewModel(PluginUpdateNotification notification, NotificationViewModel vm)
        {
            vm.Identifier = notification.Identifier;
            vm.Type = notification.NotificationType;
            vm.Body = $"New {notification.Name} plugin version {notification.Version} released!";
            if(notification.UpdateDownloaded)
                vm.Body += " Update has automatically been scheduled to be installed on the next restart.";
            if (notification.UpdateDownloaded)
            {   
                vm.ActionLink = linkGenerator.GetPathByAction(nameof(UIServerController.Maintenance),
                    "UIServer",
                    new {command = "soft-restart"}, options.RootPath);
                vm.ActionText = "Restart now";
            }
            else
            {
                    
                vm.ActionLink = linkGenerator.GetPathByAction(nameof(UIServerController.ListPlugins),
                    "UIServer",
                    new {plugin = notification.PluginIdentifier}, options.RootPath);
            }
                
        }
    }

    public PluginUpdateNotification()
    {
    }

    public PluginUpdateNotification(PluginService.AvailablePlugin plugin, bool updateDownloaded)
    {
        UpdateDownloaded = updateDownloaded;
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
