using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Contracts;
using BTCPayServer.Models;
using BTCPayServer.Plugins;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class ServerController
    {
        [HttpGet("server/plugins")]
        public async Task<IActionResult> ListPlugins(
            [FromServices] PluginService pluginService,
            [FromServices] BTCPayServerOptions btcPayServerOptions,
            string remote = "btcpayserver/btcpayserver-plugins")
        {
            IEnumerable<PluginService.AvailablePlugin> availablePlugins;
            try
            {
                availablePlugins = await pluginService.GetRemotePlugins(remote);
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
            var res = new ListPluginsViewModel()
            {
                Installed = pluginService.LoadedPlugins,
                Available = availablePlugins,
                Remote = remote,
                Commands = pluginService.GetPendingCommands(),
                CanShowRestart = btcPayServerOptions.DockerDeployment
            };
            return View(res);
        }

        public class ListPluginsViewModel
        {
            public string Remote { get; set; }
            public IEnumerable<IBTCPayServerPlugin> Installed { get; set; }
            public IEnumerable<PluginService.AvailablePlugin> Available { get; set; }
            public (string command, string plugin)[] Commands { get; set; }
            public bool CanShowRestart { get; set; }
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
            [FromServices] PluginService pluginService, string remote, string plugin)
        {
            try
            {
                await pluginService.DownloadRemotePlugin(remote, plugin);
                pluginService.InstallPlugin(plugin);
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
                    Message = "The plugin could not be downloaded. Try again later.", Severity = StatusMessageModel.StatusSeverity.Error
                });
            }

            return RedirectToAction("ListPlugins");
        }


        [HttpPost("server/plugins/upload")]
        public async Task<IActionResult> UploadPlugin([FromServices] PluginService pluginService,
            List<IFormFile> files)
        {
            foreach (var formFile in files.Where(file => file.Length > 0))
            {
                await pluginService.UploadPlugin(formFile);
                pluginService.InstallPlugin(formFile.FileName.TrimEnd(PluginManager.BTCPayPluginSuffix,
                    StringComparison.InvariantCultureIgnoreCase));
            }

            return RedirectToAction("ListPlugins",
                new {StatusMessage = "Files uploaded, restart server to load plugins"});
        }
    }
}
