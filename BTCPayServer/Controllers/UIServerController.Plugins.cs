using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Plugins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static BTCPayServer.Plugins.PluginService;

namespace BTCPayServer.Controllers;

public partial class UIServerController
{
    [HttpGet("server/plugins/manage")]
    public async Task<IActionResult> ManagePlugins(
        [FromServices] PluginService pluginService)
    {
        IEnumerable<AvailablePlugin> availablePlugins;
        try
        {
            availablePlugins = await pluginService.GetRemotePlugins();
        }
        catch (Exception)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = "Remote plugins lookup failed. Try again later."
            });
            availablePlugins = Array.Empty<AvailablePlugin>();
        }
        var availablePluginsByIdentifier = new Dictionary<string, AvailablePlugin>();
        foreach (var p in availablePlugins)
            availablePluginsByIdentifier.TryAdd(p.Identifier, p);
        var res = new ListPluginsViewModel
        {
            Installed = pluginService.LoadedPlugins,
            Available = availablePlugins,
            Commands = pluginService.GetPendingCommands(),
            Disabled = pluginService.GetDisabledPlugins(),
            CanShowRestart = true,
            DownloadedPluginsByIdentifier = availablePluginsByIdentifier
        };
        return View(res);
    }

    [HttpGet("server/plugins/explore")]
    public async Task<IActionResult> ExplorePlugins(
        [FromServices] PluginService pluginService, ListPluginsViewModel model)
    {
        model ??= new ListPluginsViewModel();
        IEnumerable<AvailablePlugin> availablePlugins;
        try
        {
            availablePlugins = await pluginService.GetRemotePlugins();
        }
        catch (Exception)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = "Remote plugins lookup failed. Try again later."
            });
            availablePlugins = Array.Empty<AvailablePlugin>();
        }
        if (!string.IsNullOrEmpty(model.SearchText))
        {
            availablePlugins = availablePlugins.Where(c => c.Name.ToLower().Contains(model.SearchText.ToLower()));
        }
        var availablePluginsByIdentifier = new Dictionary<string, AvailablePlugin>();
        foreach (var p in availablePlugins)
            availablePluginsByIdentifier.TryAdd(p.Identifier, p);

        model.Installed = pluginService.LoadedPlugins;
        model.Available = availablePlugins;
        model.CanShowRestart = true;
        model.DownloadedPluginsByIdentifier = availablePluginsByIdentifier;
        return View(model);
    }

    [HttpPost("server/plugins/uninstall")]
    public async Task<IActionResult> UnInstallPlugin(
        [FromServices] PluginService pluginService, string plugin)
    {
        await pluginService.UninstallPlugin(plugin);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Plugin scheduled to be uninstalled.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction("ManagePlugins");
    }

    [HttpPost("server/plugins/cancel")]
    public IActionResult CancelPluginCommands(
        [FromServices] PluginService pluginService, string plugin)
    {
        pluginService.CancelCommands(plugin);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Plugin action cancelled.",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction("ManagePlugins");
    }

    [HttpPost("server/plugins/install")]
    public async Task<IActionResult> InstallPlugin(
        [FromServices] PluginService pluginService, string plugin, bool update = false, string version = null)
    {
        try
        {
            await pluginService.DownloadRemotePlugin(plugin, version);
            if (update)
            {
                pluginService.UpdatePlugin(plugin);
            }
            else
            {
                await pluginService.InstallPlugin(plugin);
            }
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Plugin scheduled to be installed.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        catch (Exception)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "The plugin could not be downloaded. Try again later.",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
        }
        return RedirectToAction("ManagePlugins");
    }

    [HttpPost("server/plugins/upload")]
    public async Task<IActionResult> UploadPlugin([FromServices] PluginService pluginService,
        List<IFormFile> files)
    {
        foreach (var formFile in files.Where(file => file.Length > 0).Where(file => file.FileName.IsValidFileName()))
        {
            await pluginService.UploadPlugin(formFile);
            await pluginService.InstallPlugin(formFile.FileName.TrimEnd(PluginManager.BTCPayPluginSuffix,
                StringComparison.InvariantCultureIgnoreCase));
        }

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Files uploaded, restart server to load plugins",
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction("ManagePlugins");
    }
}
