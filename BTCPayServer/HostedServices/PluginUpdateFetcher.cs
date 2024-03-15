using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.HostedServices
{
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
            var notify = new Dictionary<string, string>();
            
            foreach (var pair in remotePluginsList)
            {
                if (dh.LastVersions.TryGetValue(pair.Key, out var lastVersion) && lastVersion >= pair.Value)
                    continue;
                if (installedPlugins.TryGetValue(pair.Key, out var installedVersion) && installedVersion < pair.Value)
                {
                    notify.TryAdd(pair.Key, "update");
                }
                else if (disabledPlugins.TryGetValue(pair.Key, out var disabledVersion) && disabledVersion.Item1 < pair.Value)
                {
                    notify.TryAdd(pair.Key, "update");
                }
            }

            dh.LastVersions = remotePluginsList;
            //check if any loaded plugin is in the remote list with exact version and is marked with Kill. If so, check if there is the plugin listed under AutoKillSwitch and if so, kill the plugin
            foreach (var plugin in pluginService.LoadedPlugins)
            {
                var matched = remotePlugins.FirstOrDefault(p => p.Identifier == plugin.Identifier && p.Version == plugin.Version);
                if(matched is {Kill: true} && dh.KillswitchPlugins.Contains(plugin.Identifier))
                {
                    PluginManager.DisablePlugin(dataDirectories.PluginDir, plugin.Identifier, "Killswitch");
                    
                    notify.TryAdd(plugin.Identifier, "kill");
                }
            }
            foreach (var pluginUpdate in notify)
            {
                var plugin = remotePlugins.First(p => p.Identifier == pluginUpdate.Key);

                if (pluginUpdate.Value == "kill")
                {
                    await notificationSender.SendNotification(new AdminScope(), new PluginKillNotification(plugin));
                }
                else
                {
                    var update = false;
                    if (dh.AutoUpdatePlugins.Contains(plugin.Identifier))
                    {
                        update = true;
                        await pluginService.DownloadRemotePlugin(plugin.Identifier, plugin.Version.ToString());
                        pluginService.UpdatePlugin(plugin.Identifier);
                    }
                    await notificationSender.SendNotification(new AdminScope(), new PluginUpdateNotification(plugin, update));
                }
                
            }

            await settingsRepository.UpdateSetting(dh);
            
           
            
        }
    }
}
