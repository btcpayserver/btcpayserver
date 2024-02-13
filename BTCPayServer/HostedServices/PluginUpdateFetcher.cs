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

    public class PluginVersionCheckerDataHolder
    {
        public Dictionary<string, Version> LastVersions { get; set; }
        public List<string> AutoUpdatePlugins { get; set; }
        public List<string> KillswitchPlugins { get; set; }
    }

    public class PluginUpdateFetcher(SettingsRepository settingsRepository, NotificationSender notificationSender, PluginService pluginService, DataDirectories dataDirectories)
        : IPeriodicTask
    {
        public async Task Do(CancellationToken cancellationToken)
        {
            var dh = await settingsRepository.GetSettingAsync<PluginVersionCheckerDataHolder>() ??
                     new PluginVersionCheckerDataHolder();
            dh.LastVersions ??= new Dictionary<string, Version>();
            dh.AutoUpdatePlugins ??= new List<string>();
            var disabledPlugins = pluginService.GetDisabledPlugins();

            var installedPlugins =
                pluginService.LoadedPlugins.ToDictionary(plugin => plugin.Identifier, plugin => plugin.Version);
            var remotePlugins = await pluginService.GetRemotePlugins();
            //take the latest version of each plugin
            var remotePluginsList = remotePlugins
                .GroupBy(plugin => plugin.Identifier)
                .Select(group => group.OrderByDescending(plugin => plugin.Version).First())
                .Where(pair => installedPlugins.ContainsKey(pair.Identifier) || disabledPlugins.ContainsKey(pair.Name))
                .ToDictionary(plugin => plugin.Identifier, plugin => plugin.Version);
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
            //check if any loaded plugin is in the remote list with exact version and is marked with Kill. If so, check if there is the plugin listed under AutoKillSwitch and if so, kill the plugin
            foreach (var plugin in pluginService.LoadedPlugins)
            {
                var matched = remotePlugins.FirstOrDefault(p => p.Identifier == plugin.Identifier && p.Version == plugin.Version);
                if(matched is {Kill: true} && dh.KillswitchPlugins.Contains(plugin.Identifier))
                {
                    PluginManager.DisablePlugin(dataDirectories.PluginDir, plugin.Identifier);
                }
            }
            foreach (string pluginUpdate in notify)
            {
                var plugin = remotePlugins.First(p => p.Identifier == pluginUpdate);
                var update = false;
                if (dh.AutoUpdatePlugins.Contains(plugin.Identifier))
                {
                    update = true;
                    await pluginService.DownloadRemotePlugin(plugin.Identifier, plugin.Version.ToString());
                    pluginService.UpdatePlugin(plugin.Identifier);
                }
                await notificationSender.SendNotification(new AdminScope(), new PluginUpdateNotification(plugin, update));
            }

            await settingsRepository.UpdateSetting(dh);
            
           
            
        }
    }
}
