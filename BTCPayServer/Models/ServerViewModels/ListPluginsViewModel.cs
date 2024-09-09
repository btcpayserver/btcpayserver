using System;
using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins;

namespace BTCPayServer.Models.ServerViewModels;

public class ListPluginsViewModel
{
    public string SearchText { get; set; }
    public IEnumerable<IBTCPayServerPlugin> Installed { get; set; }
    public IEnumerable<PluginService.AvailablePlugin> Available { get; set; }
    public (string command, string plugin)[] Commands { get; set; }
    public bool CanShowRestart { get; set; }
    public Dictionary<string, Version> Disabled { get; set; }
    public Dictionary<string, PluginService.AvailablePlugin> DownloadedPluginsByIdentifier { get; set; } = new();
}
