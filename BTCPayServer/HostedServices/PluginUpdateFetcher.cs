using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices
{
    internal class PluginUpdateNotification : BaseNotification
    {
        private const string TYPE = "pluginupdate";

        internal class Handler : NotificationHandler<PluginUpdateNotification>
        {
            private readonly LinkGenerator _linkGenerator;
            private readonly BTCPayServerOptions _options;

            public Handler(LinkGenerator linkGenerator, BTCPayServerOptions options)
            {
                _linkGenerator = linkGenerator;
                _options = options;
            }

            public override string NotificationType => TYPE;

            public override (string identifier, string name)[] Meta
            {
                get
                {
                    return new (string identifier, string name)[] {(TYPE, "Plugin update")};
                }
            }

            protected override void FillViewModel(PluginUpdateNotification notification, NotificationViewModel vm)
            {
                vm.Identifier = notification.Identifier;
                vm.Type = notification.NotificationType;
                vm.Body = $"New {notification.Name} plugin version {notification.Version} released!";
                vm.ActionLink = _linkGenerator.GetPathByAction(nameof(UIServerController.ListPlugins),
                    "UIServer",
                    new {plugin = notification.PluginIdentifier}, _options.RootPath);
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

    public class PluginUpdateFetcher : IPeriodicTask
    {
        public PluginUpdateFetcher(
            SettingsRepository settingsRepository,
            ILogger<PluginUpdateFetcher> logger, NotificationSender notificationSender, PluginService pluginService)
        {
            _settingsRepository = settingsRepository;
            _logger = logger;
            _notificationSender = notificationSender;
            _pluginService = pluginService;
        }

        private readonly SettingsRepository _settingsRepository;
        private readonly ILogger<PluginUpdateFetcher> _logger;
        private readonly NotificationSender _notificationSender;
        private readonly PluginService _pluginService;


        public async Task Do(CancellationToken cancellationToken)
        {
            var dh = await _settingsRepository.GetSettingAsync<PluginVersionCheckerDataHolder>() ??
                     new PluginVersionCheckerDataHolder();
            dh.LastVersions ??= new Dictionary<string, Version>();
            var disabledPlugins = _pluginService.GetDisabledPlugins();

            var installedPlugins =
                _pluginService.LoadedPlugins.ToDictionary(plugin => plugin.Identifier, plugin => plugin.Version);
            var remotePlugins = await _pluginService.GetRemotePlugins();
            var remotePluginsList = remotePlugins
                .Where(pair => installedPlugins.ContainsKey(pair.Identifier) || disabledPlugins.Contains(pair.Name))
                .ToDictionary(plugin => plugin.Identifier, plugin => plugin.Version);
            var notify = new HashSet<string>();
            foreach (var pair in remotePluginsList)
            {
                if (dh.LastVersions.TryGetValue(pair.Key, out var lastVersion) && lastVersion >= pair.Value)
                    continue;
                if (installedPlugins.TryGetValue(pair.Key, out var installedVersion) && installedVersion < pair.Value)
                    notify.Add(pair.Key);
                if (disabledPlugins.Contains(pair.Key))
                {
                    notify.Add(pair.Key);
                }
            }

            dh.LastVersions = remotePluginsList;

            foreach (string pluginUpdate in notify)
            {
                var plugin = remotePlugins.First(p => p.Identifier == pluginUpdate);
                await _notificationSender.SendNotification(new AdminScope(), new PluginUpdateNotification(plugin));
            }

            await _settingsRepository.UpdateSetting(dh);
        }
    }
}
