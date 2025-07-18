using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3.Model;
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
            [FromServices] BTCPayServerOptions btcPayServerOptions,
            string search = null)
        {
            IEnumerable<PluginService.AvailablePlugin> availablePlugins;
            try
            {
                availablePlugins = await pluginService.GetRemotePlugins(search);
            }
            catch (Exception)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = StringLocalizer["Remote plugins lookup failed. Try again later."].Value
                });
                availablePlugins = Array.Empty<PluginService.AvailablePlugin>();
            }
            var availablePluginsByIdentifier = new Dictionary<string, AvailablePlugin>();
            foreach (var p in availablePlugins)
                availablePluginsByIdentifier.TryAdd(p.Identifier, p);
            var res = new ListPluginsViewModel()
            {
                Plugins = pluginService.LoadedPlugins,
                Installed = pluginService.Installed,
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
            public IEnumerable<IBTCPayServerPlugin> Plugins { get; set; }
            public IEnumerable<PluginService.AvailablePlugin> Available { get; set; }
            public (string command, string plugin)[] Commands { get; set; }
            public bool CanShowRestart { get; set; }
            public Dictionary<string, Version> Disabled { get; set; }
            public Dictionary<string, AvailablePlugin> DownloadedPluginsByIdentifier { get; set; } = new Dictionary<string, AvailablePlugin>();
            public Dictionary<string, Version> Installed { get; set; }
        }

        [HttpPost("server/plugins/uninstall-all")]
        public IActionResult UnInstallAllDisabledPlugin(
            [FromServices] PluginService pluginService, string plugin)
        {
            var disabled = pluginService.GetDisabledPlugins();
            foreach (var d in disabled)
                pluginService.UninstallPlugin(d.Key);
            return RedirectToAction(nameof(ListPlugins));
        }

        [HttpPost("server/plugins/uninstall")]
        public IActionResult UnInstallPlugin(
            [FromServices] PluginService pluginService, string plugin)
        {
            pluginService.UninstallPlugin(plugin);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = StringLocalizer["Plugin scheduled to be uninstalled."].Value,
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction("ListPlugins");
        }

        [HttpPost("server/plugins/cancel")]
        public IActionResult CancelPluginCommands(
            [FromServices] PluginService pluginService, string plugin)
        {
            pluginService.CancelCommands(plugin);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = StringLocalizer["Plugin action cancelled."].Value,
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction("ListPlugins");
        }

        [HttpPost("server/plugins/install")]
        public async Task<IActionResult> InstallPlugin(
            [FromServices] PluginService pluginService, string plugin, bool update = false, string version = null)
        {
            var ctx = new DownloadPluginContext(pluginService, plugin, version, new(), new());
            await DownloadPluginAndDepedencies(ctx);
            if (ctx.DependencyFailed.Count == 0)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = StringLocalizer["Plugin scheduled to be installed."].Value,
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }
            else
            {
                var error = String.Join(" \n", ctx.DependencyFailed
                    .Select(d => $"{d.Key}: {d.Value}")
                    .ToArray());
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = StringLocalizer["The plugin could not be downloaded. Try again later."].Value + " \n" + error,
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
            }

            return RedirectToAction("ListPlugins");
        }

        public record DownloadPluginContext(PluginService PluginService, string Plugin, string Version, Dictionary<string, AvailablePlugin> Downloaded, Dictionary<string, string> DependencyFailed);
        private async Task DownloadPluginAndDepedencies(DownloadPluginContext ctx)
        {
            if (ctx.Downloaded.ContainsKey(ctx.Plugin)
                ||
                ctx.DependencyFailed.ContainsKey(ctx.Plugin))
                return;
            AvailablePlugin manifest;
            try
            {
                manifest = await ctx.PluginService.DownloadRemotePlugin(ctx.Plugin, ctx.Version);
            }
            catch(Exception ex)
            {
                ctx.DependencyFailed.Add(ctx.Plugin, ex.Message);
                return;
            }

            foreach (var dep in manifest.Dependencies)
            {
                if (!PluginManager.DependencyMet(dep, ctx.PluginService.Installed))
                {
                    var childCtx = ctx with
                    {
                        Plugin = dep.Identifier,
                        Version = GetVersion(dep.ParseCondition())?.ToString()
                    };
                    await DownloadPluginAndDepedencies(childCtx);
                    if (childCtx.DependencyFailed.ContainsKey(childCtx.Plugin))
                    {
                        ctx.DependencyFailed.Add(ctx.Plugin, $"Failed to download dependency {dep.Identifier}");
                        return;
                    }
                }
            }

            ctx.PluginService.InstallPlugin(ctx.Plugin);
            ctx.Downloaded.Add(ctx.Plugin, manifest);
        }

        // Trying to get the version from the condition. Not perfect, but good enough for most cases.
        private Version GetVersion(VersionCondition condition)
        {
            if (condition is VersionCondition.Op op)
            {
                if (op.Operation is ">=" or ">" or "^" or "~")
                    return null; // Take the highest we can
                if (op.Operation is "==")
                    return op.Version;
            }
            else if (condition is VersionCondition.Yes)
                return null;
            else if (condition is VersionCondition.Multiple m)
                return m.Conditions.Select(GetVersion).FirstOrDefault();
            return null;
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

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = StringLocalizer["Files uploaded, restart server to load plugins"].Value,
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            return RedirectToAction("ListPlugins");
        }
    }
}
