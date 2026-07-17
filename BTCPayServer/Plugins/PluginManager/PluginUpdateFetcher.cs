using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins;
using BTCPayServer.Plugins.PluginManagement;
using BTCPayServer.Plugins.PluginManagement.Controllers;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.HostedServices
{
    internal class PluginUpdateNotification : BaseNotification
    {
        private const string Type = "pluginupdate";

        internal class Handler(LinkGenerator linkGenerator, BTCPayServerOptions options, IStringLocalizer StringLocalizer) : NotificationHandler<PluginUpdateNotification>
        {
            public override string NotificationType => Type;

            public override (string identifier, string name)[] Meta => [(Type, StringLocalizer["Plugin update"])];

            protected override void FillViewModel(PluginUpdateNotification notification, NotificationViewModel vm)
            {
                vm.Identifier = notification.Identifier;
                vm.Type = notification.NotificationType;
                vm.Body = StringLocalizer["New {0} plugin version {1} released!", notification.Name, notification.Version];
                vm.ActionLink = linkGenerator.GetPathByAction(
                    action: nameof(UIPluginManagerController.ListPlugins),
                    controller: "UIPluginManager",
                    values: new { area = PluginManagerPlugin.Area },
                    pathBase: options.RootPath,
                    fragment: new FragmentString($"#{Uri.EscapeDataString(notification.PluginIdentifier)}"));
            }
        }

        public PluginUpdateNotification()
        {
        }

        public PluginUpdateNotification(PluginService.AvailablePlugin plugin)
        {
            Name = plugin.Name;
            PluginIdentifier = plugin.Identifier;
            Version = plugin.Version.ToString();
        }

        public string PluginIdentifier { get; set; }

        public string Name { get; set; }

        public string Version { get; set; }
        public override string Identifier => Type;
        public override string NotificationType => Type;
    }

    public class PluginVersionCheckerDataHolder
    {
        public Dictionary<string, Version> LastVersions { get; set; }
    }

    public class PluginUpdateFetcher(SettingsRepository settingsRepository, NotificationSender notificationSender, PluginService pluginService)
        : IPeriodicTask
    {
        public async Task Do(CancellationToken cancellationToken)
        {
            var dh = await settingsRepository.GetSettingAsync<PluginVersionCheckerDataHolder>() ??
                     new PluginVersionCheckerDataHolder();
            dh.LastVersions = NormalizeVersions(dh.LastVersions);
            var disabledPlugins = NormalizeVersions(pluginService.GetDisabledPlugins());

            var installedPlugins = pluginService.Installed;
            var remotePlugins = await pluginService.GetRemotePlugins(null, cancellationToken);
            var latestRemotePlugins = remotePlugins
                .GroupBy(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(plugin => plugin.Version).First())
                .Where(pair => installedPlugins.ContainsKey(pair.Identifier) || disabledPlugins.ContainsKey(pair.Identifier))
                .ToArray();
            foreach (var plugin in latestRemotePlugins)
            {
                if (dh.LastVersions.TryGetValue(plugin.Identifier, out var lastVersion) && lastVersion >= plugin.Version)
                    continue;

                var hasUpdate = installedPlugins.TryGetValue(plugin.Identifier, out var installedVersion)
                    ? installedVersion < plugin.Version
                    : disabledPlugins.TryGetValue(plugin.Identifier, out var disabledVersion) && disabledVersion < plugin.Version;
                if (hasUpdate)
                    await notificationSender.SendNotification(new AdminScope(), new PluginUpdateNotification(plugin));
            }

            dh.LastVersions = latestRemotePlugins.ToDictionary(
                plugin => plugin.Identifier,
                plugin => plugin.Version,
                StringComparer.OrdinalIgnoreCase);

            await settingsRepository.UpdateSetting(dh);
        }

        private static Dictionary<string, Version> NormalizeVersions(Dictionary<string, Version> versions)
        {
            return (versions ?? new Dictionary<string, Version>())
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.First().Key,
                    group => group.OrderByDescending(pair => pair.Value).First().Value,
                    StringComparer.OrdinalIgnoreCase);
        }
    }
}
