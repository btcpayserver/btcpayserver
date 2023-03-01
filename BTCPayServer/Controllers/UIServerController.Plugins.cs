using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static BTCPayServer.Plugins.PluginService;

namespace BTCPayServer.Controllers
{
    public partial class UIServerController
    {
        [HttpGet("server/plugins")]
        public async Task<IActionResult> ListPlugins(
            [FromServices] PluginService pluginService,
            [FromServices] BTCPayServerOptions btcPayServerOptions)
        {
            IEnumerable<PluginService.AvailablePlugin> availablePlugins;
            try
            {
                availablePlugins = await pluginService.GetRemotePlugins();
            }
            catch (Exception)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = "Remote plugins lookup failed. Try again later."
                });
                availablePlugins = Array.Empty<PluginService.AvailablePlugin>();
            }
            var availablePluginsByIdentifier = new Dictionary<string, AvailablePlugin>();
            foreach (var p in availablePlugins)
                availablePluginsByIdentifier.TryAdd(p.Identifier, p);
            var res = new ListPluginsViewModel()
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

        public class ListPluginsViewModel
        {
            public IEnumerable<IBTCPayServerPlugin> Installed { get; set; }
            public IEnumerable<PluginService.AvailablePlugin> Available { get; set; }
            public (string command, string plugin)[] Commands { get; set; }
            public bool CanShowRestart { get; set; }
            public string[] Disabled { get; set; }
            public Dictionary<string, AvailablePlugin> DownloadedPluginsByIdentifier { get; set; } = new Dictionary<string, AvailablePlugin>();
        }

        [HttpPost("server/plugins/uninstall")]
        public IActionResult UnInstallPlugin(
            [FromServices] PluginService pluginService, string plugin)
        {
            pluginService.UninstallPlugin(plugin);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Plugin scheduled to be uninstalled.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction("ListPlugins");
        }

        [HttpPost("server/plugins/cancel")]
        public IActionResult CancelPluginCommands(
            [FromServices] PluginService pluginService, string plugin)
        {
            pluginService.CancelCommands(plugin);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Plugin action cancelled.",
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction("ListPlugins");
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
                    pluginService.InstallPlugin(plugin);
                }
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = "Plugin scheduled to be installed.",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }
            catch (Exception)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = "The plugin could not be downloaded. Try again later.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
            }

            return RedirectToAction("ListPlugins");
        }

        [HttpPost("server/plugins/upload")]
        public async Task<IActionResult> UploadPlugin([FromServices] PluginService pluginService,
            List<IFormFile> files)
        {
            foreach (var formFile in files.Where(file => file.Length > 0).Where(file => file.FileName.IsValidFileName()))
            {
                await pluginService.UploadPlugin(formFile);
                pluginService.InstallPlugin(formFile.FileName.TrimEnd(PluginManager.BTCPayPluginSuffix,
                    StringComparison.InvariantCultureIgnoreCase));
            }

            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Files uploaded, restart server to load plugins",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction("ListPlugins");
        }
    }
}
