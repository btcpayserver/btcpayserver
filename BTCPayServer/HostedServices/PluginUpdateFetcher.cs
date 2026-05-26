using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.HostedServices
{
    internal class PluginUpdateNotification : BaseNotification
    {
        private const string TYPE = "pluginupdate";

        internal class Handler(LinkGenerator linkGenerator, BTCPayServerOptions options, IStringLocalizer stringLocalizer) : NotificationHandler<PluginUpdateNotification>
        {
            private IStringLocalizer StringLocalizer { get; } = stringLocalizer;

            public override string NotificationType => TYPE;

            public override (string identifier, string name)[] Meta
            {
                get
                {
                    return new (string identifier, string name)[] {(TYPE, StringLocalizer["Plugin update"])};
                }
            }

            protected override void FillViewModel(PluginUpdateNotification notification, NotificationViewModel vm)
            {
                vm.Identifier = notification.Identifier;
                vm.Type = notification.NotificationType;
                vm.Body = StringLocalizer["New {0} plugin version {1} released!", notification.Name, notification.Version];
                var actionLink = linkGenerator.GetPathByAction(
                    nameof(UIServerController.ListPlugins),
                    "UIServer",
                    pathBase: options.RootPath);
                vm.ActionLink = actionLink is null
                    ? null
                    : $"{actionLink}#{Uri.EscapeDataString(notification.PluginIdentifier)}";
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
        public override string Identifier => TYPE;
        public override string NotificationType => TYPE;
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

            var installedPlugins = NormalizeVersions(pluginService.Installed);
            var remotePlugins = await pluginService.GetRemotePlugins(null, cancellationToken);
            //take the latest version of each plugin
            var remotePluginsByIdentifier = remotePlugins
                .GroupBy(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(plugin => plugin.Version).First())
                .Where(pair => installedPlugins.ContainsKey(pair.Identifier) || disabledPlugins.ContainsKey(pair.Identifier))
                .ToDictionary(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase);
            var remotePluginsList = remotePluginsByIdentifier.ToDictionary(plugin => plugin.Key, plugin => plugin.Value.Version, StringComparer.OrdinalIgnoreCase);
            var notify = new HashSet<string>();
            foreach (var pair in remotePluginsList)
            {
                if (dh.LastVersions.TryGetValue(pair.Key, out var lastVersion) && lastVersion >= pair.Value)
                    continue;
                if (installedPlugins.TryGetValue(pair.Key, out var installedVersion) && installedVersion < pair.Value)
                {
                    notify.Add(pair.Key);
                }
                else if (disabledPlugins.TryGetValue(pair.Key, out var disabledVersion) && disabledVersion < pair.Value)
                {
                    notify.Add(pair.Key);
                }
            }

            dh.LastVersions = remotePluginsList;

            foreach (string pluginUpdate in notify)
            {
                var plugin = remotePluginsByIdentifier[pluginUpdate];
                await notificationSender.SendNotification(new AdminScope(), new PluginUpdateNotification(plugin));
            }

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
