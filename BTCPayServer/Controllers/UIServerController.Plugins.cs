using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static BTCPayServer.Plugins.PluginService;

namespace BTCPayServer.Controllers;

public partial class UIServerController
{
    [HttpGet("server/plugins")]
    public async Task<IActionResult> ListPlugins(
        [FromServices] PluginService pluginService,
        [FromServices] PluginManagementProjectionService projectionService)
    {
        var source = await BuildPluginProjectionSource(
            pluginService,
            selectedSlug: null,
            setErrorStatusMessage: true);
        var model = projectionService.CreateInstalledPluginsViewModel(source);
        return View(model);
    }

    [HttpGet("server/plugins/directory")]
    public async Task<IActionResult> PluginDirectory(
        [FromServices] PluginService pluginService,
        [FromServices] PluginManagementProjectionService projectionService,
        [FromServices] PoliciesSettings policiesSettings,
        string selectedSlug = null)
    {
        var source = await BuildPluginProjectionSource(
            pluginService,
            selectedSlug: selectedSlug,
            setErrorStatusMessage: true,
            loadRemotePlugins: !string.IsNullOrWhiteSpace(selectedSlug));
        var model = projectionService.CreatePluginDirectoryViewModel(source);
        var pluginSourceBaseUri = pluginService.GetPluginSourceBaseUri();
        // The embedded directory iframe trusts the admin-configured plugin source.
        model.DirectoryOrigin = pluginSourceBaseUri is null ? null : $"{pluginSourceBaseUri.Scheme}://{pluginSourceBaseUri.Authority}";
        model.PanelUrl = Url.Action(nameof(SelectedPluginPanel));
        var btcpayVersion = pluginService.GetShortBtcpayVersion();
        var preReleaseEnabled = policiesSettings.PluginPreReleases;
        model.DirectoryIframeUrl = BuildDirectoryIframeUrl(
            pluginSourceBaseUri,
            btcpayVersion,
            preReleaseEnabled);
        model.SelectedPluginPanel.EmbeddedDetailsUrl = BuildPluginDetailsEmbedUrl(
            pluginSourceBaseUri,
            model.SelectedPluginPanel.SelectedSlug,
            model.SelectedPluginPanel.Actions.FirstOrDefault(action => action.FormAction == nameof(InstallPlugin))?.Version,
            btcpayVersion,
            preReleaseEnabled);
        return View(model);
    }

    [HttpGet("server/plugins/panel")]
    public async Task<IActionResult> SelectedPluginPanel(
        [FromServices] PluginService pluginService,
        [FromServices] PluginManagementProjectionService projectionService,
        [FromServices] PoliciesSettings policiesSettings,
        string slug = null)
    {
        var source = await BuildPluginProjectionSource(
            pluginService,
            slug,
            setErrorStatusMessage: false,
            loadRemotePlugins: !string.IsNullOrWhiteSpace(slug));
        var model = projectionService.CreateSelectedPluginPanelViewModel(source);
        model.EmbeddedDetailsUrl = BuildPluginDetailsEmbedUrl(
            pluginService.GetPluginSourceBaseUri(),
            model.SelectedSlug,
            model.Actions.FirstOrDefault(action => action.FormAction == nameof(InstallPlugin))?.Version,
            pluginService.GetShortBtcpayVersion(),
            policiesSettings.PluginPreReleases);
        return PartialView("_SelectedPluginPanel", model);
    }

    [HttpPost("server/plugins/uninstall-all")]
    public IActionResult UnInstallAllDisabledPlugin(
        [FromServices] PluginService pluginService)
    {
        var disabled = pluginService.GetDisabledPlugins();
        foreach (var d in disabled)
            pluginService.UninstallPlugin(d.Key);
        return RedirectToAction(nameof(ListPlugins));
    }

    [HttpPost("server/plugins/uninstall")]
    public IActionResult UnInstallPlugin(
        [FromServices] PluginService pluginService,
        string plugin)
    {
        pluginService.UninstallPlugin(plugin);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = StringLocalizer["Plugin scheduled to be uninstalled."].Value,
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(ListPlugins));
    }

    [HttpPost("server/plugins/enable")]
    public IActionResult EnablePlugin(
        [FromServices] PluginService pluginService,
        string plugin)
    {
        pluginService.EnablePlugin(plugin);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = StringLocalizer["Plugin scheduled to be enabled."].Value,
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(ListPlugins));
    }

    [HttpPost("server/plugins/cancel")]
    public IActionResult CancelPluginCommands(
        [FromServices] PluginService pluginService,
        string plugin,
        string selectedSlug = null,
        string returnTo = null)
    {
        pluginService.CancelCommands(plugin);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = StringLocalizer["Plugin action cancelled."].Value,
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToPlugins(returnTo, selectedSlug);
    }

    [HttpPost("server/plugins/install")]
    public async Task<IActionResult> InstallPlugin(
        [FromServices] PluginService pluginService,
        string plugin,
        string version = null,
        string selectedSlug = null,
        string returnTo = null)
    {
        var ctx = new DownloadPluginContext(pluginService, plugin, version, new(), new(), null);
        await DownloadPluginAndDependencies(ctx);
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

        return RedirectToPlugins(returnTo, selectedSlug);
    }

    public record DownloadPluginContext(PluginService PluginService, string Plugin, string Version, Dictionary<string, AvailablePlugin> Downloaded, Dictionary<string, string> DependencyFailed, VersionCondition VersionCondition);
    private async Task DownloadPluginAndDependencies(DownloadPluginContext ctx)
    {
        if (ctx.Downloaded.ContainsKey(ctx.Plugin)
            ||
            ctx.DependencyFailed.ContainsKey(ctx.Plugin))
            return;
        AvailablePlugin manifest;
        try
        {
            manifest = await ctx.PluginService.DownloadRemotePlugin(ctx.Plugin, ctx.Version, ctx.VersionCondition);
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
                if (dep.Identifier.Equals("BTCPayServer", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.DependencyFailed.Add(ctx.Plugin, $"This condition can't be satisfied {dep}");
                    return;
                }

                var cond = dep.ParseCondition();
                var childCtx = ctx with
                {
                    Plugin = dep.Identifier,
                    Version = null,
                    VersionCondition = cond
                };
                if (childCtx.VersionCondition is VersionCondition.Not)
                {
                    ctx.DependencyFailed.Add(ctx.Plugin, $"The currently installed plugin {dep.Identifier} is incompatible with this plugin.");
                    return;
                }

                await DownloadPluginAndDependencies(childCtx);
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
        return RedirectToAction(nameof(ListPlugins));
    }

    private async Task<PluginManagementProjectionService.ProjectionSource> BuildPluginProjectionSource(
        PluginService pluginService,
        string selectedSlug,
        bool setErrorStatusMessage,
        bool loadRemotePlugins = true)
    {
        AvailablePlugin[] allPlugins = [];
        try
        {
            if (loadRemotePlugins)
            {
                allPlugins = await pluginService.GetRemotePlugins(null);
            }
        }
        catch (Exception ex)
        {
            if (setErrorStatusMessage)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = StringLocalizer["Remote plugins lookup failed. Try again later. Error: {0}", ex.Message].Value
                });
            }
        }
        return new PluginManagementProjectionService.ProjectionSource
        {
            LoadedPlugins = pluginService.LoadedPlugins,
            Installed = pluginService.Installed,
            AllAvailable = allPlugins,
            Commands = pluginService.GetPendingCommands(),
            Disabled = pluginService.GetDisabledPlugins(),
            GetVersionOfPendingInstall = pluginService.GetVersionOfPendingInstall,
            SelectedPluginSlug = selectedSlug
        };
    }

    private IActionResult RedirectToPlugins(string returnTo, string selectedSlug)
    {
        var action = returnTo?.Equals("directory", StringComparison.OrdinalIgnoreCase) is true
            ? nameof(PluginDirectory)
            : nameof(ListPlugins);
        return RedirectToAction(action, new
        {
            selectedSlug
        });
    }

    internal static string BuildDirectoryIframeUrl(
        Uri pluginSourceBaseUri,
        string btcpayVersion,
        bool includePreRelease)
    {
        if (pluginSourceBaseUri is null) return null;

        var baseUri = pluginSourceBaseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? pluginSourceBaseUri
            : new Uri(pluginSourceBaseUri.AbsoluteUri + "/");
        var builder = new UriBuilder(new Uri(baseUri, "public/plugins"));
        var query = new List<string> { "embed=1" };
        if (!string.IsNullOrEmpty(btcpayVersion))
        {
            query.Add($"btcpayVersion={Uri.EscapeDataString(btcpayVersion)}");
            query.Add($"includePreRelease={includePreRelease.ToString().ToLowerInvariant()}");
        }
        builder.Query = string.Join("&", query);
        return builder.Uri.ToString();
    }

    private static string BuildPluginDetailsEmbedUrl(
        Uri pluginSourceBaseUri,
        string selectedSlug,
        string selectedVersion,
        string btcpayVersion,
        bool includePreRelease)
    {
        if (pluginSourceBaseUri is null || string.IsNullOrEmpty(selectedSlug)) return null;

        var baseUri = pluginSourceBaseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? pluginSourceBaseUri
            : new Uri(pluginSourceBaseUri.AbsoluteUri + "/");
        var builder = new UriBuilder(new Uri(baseUri, $"public/plugins/{Uri.EscapeDataString(selectedSlug)}"));
        var query = new List<string> { "embed=1" };
        if (!string.IsNullOrEmpty(btcpayVersion))
        {
            query.Add($"btcpayVersion={Uri.EscapeDataString(btcpayVersion)}");
            query.Add($"includePreRelease={includePreRelease.ToString().ToLowerInvariant()}");
        }
        if (!string.IsNullOrEmpty(selectedVersion)) query.Add($"version={Uri.EscapeDataString(selectedVersion)}");

        builder.Query = string.Join("&", query);
        return builder.Uri.ToString();
    }
}
