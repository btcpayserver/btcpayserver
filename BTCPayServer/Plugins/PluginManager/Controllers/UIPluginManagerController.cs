using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.PluginManagement.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using static BTCPayServer.Plugins.PluginService;

namespace BTCPayServer.Plugins.PluginManagement.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyServerSettings)]
[Area(PluginManagerPlugin.Area)]
[Route("server/plugins")]
public class UIPluginManagerController(
    PluginService pluginService,
    PoliciesSettings policiesSettings,
    IStringLocalizer stringLocalizer)
    : Controller
{
    [HttpGet]
    public async Task<IActionResult> ListPlugins()
    {
        var model = await CreateInstalledPluginsViewModel();
        return View(model);
    }

    [HttpGet("directory")]
    public async Task<IActionResult> PluginDirectory(string selectedSlug = null)
    {
        var remotePlugins = string.IsNullOrWhiteSpace(selectedSlug)
            ? []
            : await LoadRemotePlugins();
        var model = CreatePluginDirectoryViewModel(selectedSlug, remotePlugins);
        var pluginSourceBaseUri = pluginService.GetPluginSourceBaseUri();
        var btcpayVersion = pluginService.GetShortBtcpayVersion();
        var preReleaseEnabled = policiesSettings.PluginPreReleases;
        model.DirectoryIframeUrl = BuildDirectoryIframeUrl(
            pluginSourceBaseUri,
            btcpayVersion,
            preReleaseEnabled);
        model.SelectedPluginPanel.EmbeddedDetailsUrl = BuildPluginDetailsEmbedUrl(
            pluginSourceBaseUri,
            model.SelectedPluginPanel.SelectedSlug,
            model.SelectedPluginPanel.InstallVersion,
            btcpayVersion,
            preReleaseEnabled);
        return View(model);
    }

    [HttpGet("panel")]
    public async Task<IActionResult> SelectedPluginPanel(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return BadRequest();

        AvailablePlugin[] remotePlugins;
        try
        {
            remotePlugins = await pluginService.GetRemotePlugins(null);
        }
        catch
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var model = CreateSelectedPluginPanelViewModel(slug, remotePlugins, GetPluginRuntimeState());
        var pluginSourceBaseUri = pluginService.GetPluginSourceBaseUri();
        model.EmbeddedDetailsUrl = BuildPluginDetailsEmbedUrl(
            pluginSourceBaseUri,
            model.SelectedSlug,
            model.InstallVersion,
            pluginService.GetShortBtcpayVersion(),
            policiesSettings.PluginPreReleases);
        return PartialView("_SelectedPluginPanel", model);
    }

    [HttpPost("uninstall-all")]
    public IActionResult UnInstallAllDisabledPlugin()
    {
        var disabled = pluginService.GetDisabledPlugins();
        foreach (var d in disabled)
            pluginService.UninstallPlugin(d.Key);
        return RedirectToAction(nameof(ListPlugins));
    }

    [HttpPost("uninstall")]
    public IActionResult UnInstallPlugin(string plugin)
    {
        pluginService.UninstallPlugin(plugin);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = stringLocalizer["Plugin scheduled to be uninstalled."].Value,
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(ListPlugins));
    }

    [HttpPost("enable")]
    public IActionResult EnablePlugin(string plugin)
    {
        pluginService.EnablePlugin(plugin);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = stringLocalizer["Plugin scheduled to be enabled."].Value,
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(ListPlugins));
    }

    [HttpPost("cancel")]
    public IActionResult CancelPluginCommands(
        string plugin,
        string selectedSlug = null)
    {
        pluginService.CancelCommands(plugin);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = stringLocalizer["Plugin action cancelled."].Value,
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToPlugins(selectedSlug);
    }

    [HttpPost("install")]
    public async Task<IActionResult> InstallPlugin(
        string plugin,
        string version = null,
        string returnTo = null)
    {
        var ctx = new DownloadPluginContext(plugin, version, new(), new(), null);
        await DownloadPluginAndDependencies(ctx);
        if (ctx.DependencyFailed.Count == 0)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = stringLocalizer["Plugin scheduled to be installed."].Value,
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else
        {
            var error = String.Join(" \n", ctx.DependencyFailed
                .Select(d => $"{d.Key}: {d.Value}"));
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = stringLocalizer["The plugin could not be downloaded. Try again later."].Value + " \n" + error,
                Severity = StatusMessageModel.StatusSeverity.Error
            });
        }

        return string.Equals(returnTo, "directory", StringComparison.OrdinalIgnoreCase)
            ? RedirectToAction(nameof(PluginDirectory))
            : RedirectToAction(nameof(ListPlugins));
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadPlugin(List<IFormFile> files)
    {
        foreach (var formFile in files.Where(file => file.Length > 0).Where(file => file.FileName.IsValidFileName()))
        {
            await pluginService.UploadPlugin(formFile);
            pluginService.InstallPlugin(formFile.FileName.TrimEnd(PluginManager.BTCPayPluginSuffix,
                StringComparison.InvariantCultureIgnoreCase));
        }

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = stringLocalizer["Files uploaded, restart server to load plugins"].Value,
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(ListPlugins));
    }

    private record DownloadPluginContext(string Plugin, string Version, HashSet<string> Downloaded, Dictionary<string, string> DependencyFailed, VersionCondition VersionCondition);

    private async Task DownloadPluginAndDependencies(DownloadPluginContext ctx)
    {
        if (ctx.Downloaded.Contains(ctx.Plugin)
            ||
            ctx.DependencyFailed.ContainsKey(ctx.Plugin))
            return;
        AvailablePlugin manifest;
        try
        {
            manifest = await pluginService.DownloadRemotePlugin(ctx.Plugin, ctx.Version, ctx.VersionCondition);
        }
        catch (Exception ex)
        {
            ctx.DependencyFailed.Add(ctx.Plugin, ex.Message);
            return;
        }

        foreach (var dep in manifest.Dependencies)
        {
            if (!PluginManager.DependencyMet(dep, pluginService.Installed))
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

        pluginService.InstallPlugin(ctx.Plugin);
        ctx.Downloaded.Add(ctx.Plugin);
    }

    internal async Task<InstalledPluginsViewModel> CreateInstalledPluginsViewModel(
        IEnumerable<AvailablePlugin> remotePlugins = null)
    {
        remotePlugins ??= await LoadRemotePlugins();
        var runtimeState = GetPluginRuntimeState();
        var versionsByIdentifier = remotePlugins
            .GroupBy(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var requiredPluginIdentifiers = GetRequiredPluginIdentifiers(runtimeState, versionsByIdentifier);

        return new InstalledPluginsViewModel
        {
            DisabledPlugins = runtimeState.DisabledVersions
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => CreateDisabledViewModel(
                    pair.Key,
                    pair.Value,
                    versionsByIdentifier.GetValueOrDefault(pair.Key),
                    runtimeState.PendingCommands))
                .ToList(),
            InstalledPlugins = runtimeState.VisibleLoadedPlugins
                .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .Select(plugin => CreateInstalledPluginCardViewModel(
                    plugin,
                    versionsByIdentifier.GetValueOrDefault(plugin.Identifier),
                    runtimeState.PendingCommands,
                    requiredPluginIdentifiers.Contains(plugin.Identifier)))
                .ToList(),
            PendingActions = runtimeState.PendingCommands
                .GroupBy(tuple => tuple.plugin, StringComparer.OrdinalIgnoreCase)
                .Select(group => new PendingPluginActionViewModel
                {
                    Plugin = group.Key,
                    Action = group.Last().command
                })
                .OrderBy(action => action.Plugin, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    internal PluginDirectoryViewModel CreatePluginDirectoryViewModel(
        string selectedSlug,
        AvailablePlugin[] remotePlugins)
    {
        var runtimeState = GetPluginRuntimeState();

        return new PluginDirectoryViewModel
        {
            HiddenPluginIdentifiers = runtimeState.VisibleLoadedPlugins
                .Select(plugin => plugin.Identifier)
                .Concat(runtimeState.DisabledVersions.Keys)
                .Where(identifier => !string.IsNullOrEmpty(identifier))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(identifier => identifier, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            HasPendingActions = runtimeState.PendingCommands.Length > 0,
            SelectedPluginPanel = CreateSelectedPluginPanelViewModel(selectedSlug, remotePlugins, runtimeState)
        };
    }

    private PluginSelectedPanelViewModel CreateSelectedPluginPanelViewModel(
        string selectedSlug,
        AvailablePlugin[] remotePlugins,
        PluginRuntimeState runtimeState)
    {
        if (string.IsNullOrEmpty(selectedSlug))
            return new PluginSelectedPanelViewModel();

        var selectedAvailable = remotePlugins.FirstOrDefault(plugin =>
            plugin.CatalogSlug != null &&
            plugin.CatalogSlug.Equals(selectedSlug, StringComparison.OrdinalIgnoreCase));
        if (selectedAvailable is null)
            return new PluginSelectedPanelViewModel();

        var identifier = selectedAvailable.Identifier;
        var loadedPlugin = runtimeState.VisibleLoadedPlugins.FirstOrDefault(plugin =>
            plugin.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
        runtimeState.DisabledVersions.TryGetValue(identifier, out var disabledVersion);
        var pluginCommands = runtimeState.PendingCommands
            .Where(command => command.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var pendingAction = pluginCommands.LastOrDefault().command;
        var pendingVersion = pluginCommands.Any(command =>
                command.command.Equals("install", StringComparison.OrdinalIgnoreCase))
            ? pluginService.GetPendingPluginManifest("install", identifier)?.Version
            : null;
        var canInstall = string.IsNullOrEmpty(pendingAction) &&
                         loadedPlugin is null &&
                         disabledVersion is null;

        return new PluginSelectedPanelViewModel
        {
            SelectedSlug = selectedAvailable.CatalogSlug,
            PluginIdentifier = identifier,
            PluginName = selectedAvailable.Name,
            InstalledVersion = loadedPlugin?.Version,
            DisabledVersion = disabledVersion,
            PendingAction = pendingAction,
            PendingVersion = pendingVersion,
            InstallVersion = canInstall
                ? GetBestCandidate(remotePlugins.Where(plugin =>
                    plugin.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase)))?.Version?.ToString()
                : null
        };
    }

    private PluginRuntimeState GetPluginRuntimeState()
    {
        return new PluginRuntimeState(
            pluginService.LoadedPlugins
                .Where(plugin => !plugin.SystemPlugin)
                .ToArray(),
            pluginService.GetDisabledPlugins(),
            pluginService.GetPendingCommands());
    }

    private HashSet<string> GetRequiredPluginIdentifiers(
        PluginRuntimeState runtimeState,
        Dictionary<string, AvailablePlugin[]> availableVersionsByIdentifier)
    {
        var pendingDeletePlugins = runtimeState.PendingCommands
            .Where(tuple => tuple.command.Equals("delete", StringComparison.OrdinalIgnoreCase))
            .Select(tuple => tuple.plugin)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiredPluginIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var installedPlugin in runtimeState.VisibleLoadedPlugins)
        {
            if (pendingDeletePlugins.Contains(installedPlugin.Identifier))
                continue;

            foreach (var dependency in installedPlugin.Dependencies ?? [])
            {
                requiredPluginIdentifiers.Add(dependency.Identifier);
            }
        }

        foreach (var pendingCommand in runtimeState.PendingCommands
                     .Where(tuple => tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase) ||
                                     tuple.command.Equals("enable", StringComparison.OrdinalIgnoreCase))
                     .GroupBy(tuple => tuple.plugin, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.Last()))
        {
            var pendingPlugin = pluginService.GetPendingPluginManifest(pendingCommand.command, pendingCommand.plugin);
            if (pendingPlugin is not null)
            {
                foreach (var dependency in pendingPlugin.Dependencies ?? [])
                {
                    requiredPluginIdentifiers.Add(dependency.Identifier);
                }
                continue;
            }

            if (!availableVersionsByIdentifier.TryGetValue(pendingCommand.plugin, out var pendingAvailableVersions))
                continue;

            foreach (var dependency in pendingAvailableVersions.SelectMany(plugin => plugin.Dependencies ?? []))
            {
                requiredPluginIdentifiers.Add(dependency.Identifier);
            }
        }

        return requiredPluginIdentifiers;
    }

    private PluginDisabledViewModel CreateDisabledViewModel(
        string identifier,
        Version disabledVersion,
        IEnumerable<AvailablePlugin> availableVersions,
        (string command, string plugin)[] pendingCommands)
    {
        var bestUpdate = disabledVersion is null
            ? null
            : GetBestCandidate(availableVersions?.Where(plugin => plugin.Version > disabledVersion));
        var pluginCommands = pendingCommands
            .Where(tuple => tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var hasPendingInstall = pluginCommands.Any(tuple => tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase));
        var hasPendingDelete = pluginCommands.Any(tuple => tuple.command.Equals("delete", StringComparison.OrdinalIgnoreCase));
        var hasPendingEnable = pluginCommands.Any(tuple => tuple.command.Equals("enable", StringComparison.OrdinalIgnoreCase));
        var actions = new List<PluginActionViewModel>();
        if (hasPendingInstall)
        {
            actions.Add(CreateDisabledAction("Marked for update", "btn btn-sm btn-outline-primary"));
        }
        else if (bestUpdate is not null && !hasPendingDelete && !hasPendingEnable)
        {
            var dependenciesMet = PluginManager.DependenciesMet(bestUpdate.Dependencies ?? [], pluginService.Installed);
            actions.Add(CreateInstallAction(
                bestUpdate.Version,
                "btn btn-sm btn-primary",
                dependenciesMet ? "Update" : "Schedule update",
                dependenciesMet ? null : "Schedule upgrade for when the dependencies have been met to ensure a smooth update"));
        }

        if (hasPendingEnable)
        {
            actions.Add(CreateDisabledAction("Marked for enabling", "btn btn-sm btn-outline-primary"));
        }
        else if (!hasPendingDelete && !hasPendingInstall)
        {
            actions.Add(CreatePostAction("EnablePlugin", "Enable", "btn btn-sm btn-outline-primary"));
        }

        if (hasPendingDelete)
        {
            actions.Add(CreateDisabledAction("Marked for deletion", "btn btn-sm btn-outline-danger"));
        }
        else if (!hasPendingEnable && !hasPendingInstall)
        {
            actions.Add(CreatePostAction("UnInstallPlugin", "Uninstall", "btn btn-sm btn-outline-danger"));
        }

        return new PluginDisabledViewModel
        {
            Identifier = identifier,
            DisabledVersion = disabledVersion,
            RecommendedUpdateVersion = bestUpdate?.Version,
            Actions = actions
        };
    }

    private PluginInstalledCardViewModel CreateInstalledPluginCardViewModel(
        IBTCPayServerPlugin plugin,
        IEnumerable<AvailablePlugin> availableVersions,
        (string command, string plugin)[] pendingCommands,
        bool isRequiredByOtherPlugins)
    {
        var bestAvailable = GetBestCandidate(availableVersions);
        var bestUpdate = GetBestCandidate(availableVersions?.Where(candidate => candidate.Version > plugin.Version));
        var pluginCommands = pendingCommands
            .Where(tuple => tuple.plugin.Equals(plugin.Identifier, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var lastPendingAction = pluginCommands.LastOrDefault().command;
        var pendingInstallVersion = pluginCommands.Any(tuple => tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase))
            ? pluginService.GetPendingPluginManifest("install", plugin.Identifier)?.Version
            : null;
        var actions = new List<PluginActionViewModel>();
        if (!string.IsNullOrEmpty(lastPendingAction))
        {
            var suffix = pendingInstallVersion is null ? string.Empty : $" of {pendingInstallVersion}";
            actions.Add(new PluginActionViewModel
            {
                FormAction = "CancelPluginCommands",
                Label = $"Cancel pending {lastPendingAction}{suffix}",
                CssClass = "btn btn-outline-secondary"
            });
        }
        else
        {
            if (bestUpdate is not null)
            {
                var dependenciesMet = PluginManager.DependenciesMet(bestUpdate.Dependencies ?? [], pluginService.Installed);
                actions.Add(CreateInstallAction(
                    bestUpdate.Version,
                    "btn btn-secondary",
                    dependenciesMet ? "Update" : "Schedule update",
                    dependenciesMet ? null : "Schedule upgrade for when the dependencies have been met to ensure a smooth update"));
            }

            if (isRequiredByOtherPlugins)
            {
                actions.Add(CreateDisabledAction(
                    "Uninstall",
                    "btn btn-outline-danger",
                    "This plugin cannot be uninstalled as it is depended on by other plugins."));
            }
            else
            {
                actions.Add(CreatePostAction("UnInstallPlugin", "Uninstall", "btn btn-outline-danger"));
            }
        }

        return new PluginInstalledCardViewModel
        {
            Current = CreatePluginInfo(plugin, bestAvailable),
            Update = bestUpdate is null
                ? null
                : new PluginInfoViewModel
                {
                    Description = bestUpdate.Description,
                    Version = bestUpdate.Version,
                    Dependencies = CreateDependencyViewModels(bestUpdate.Dependencies)
                },
            PendingAction = lastPendingAction,
            Actions = actions
        };
    }

    private AvailablePlugin GetBestCandidate(IEnumerable<AvailablePlugin> plugins)
    {
        var ordered = plugins?
            .OrderByDescending(plugin => plugin.Version)
            .ToArray() ?? [];
        return ordered.FirstOrDefault(plugin => PluginManager.DependenciesMet(plugin.Dependencies ?? [], pluginService.Installed)) ?? ordered.FirstOrDefault();
    }

    private static PluginActionViewModel CreateInstallAction(
        Version version,
        string cssClass,
        string label,
        string tooltip = null)
    {
        return new PluginActionViewModel
        {
            FormAction = "InstallPlugin",
            Label = label,
            CssClass = cssClass,
            Version = version?.ToString(),
            Tooltip = tooltip
        };
    }

    private static PluginActionViewModel CreatePostAction(string action, string label, string cssClass)
    {
        return new PluginActionViewModel
        {
            FormAction = action,
            Label = label,
            CssClass = cssClass
        };
    }

    private static PluginActionViewModel CreateDisabledAction(string label, string cssClass, string tooltip = null)
    {
        return new PluginActionViewModel
        {
            Label = label,
            CssClass = cssClass,
            Tooltip = tooltip
        };
    }

    private PluginInfoViewModel CreatePluginInfo(
        IBTCPayServerPlugin plugin,
        AvailablePlugin metadata)
    {
        return new PluginInfoViewModel
        {
            Identifier = plugin.Identifier,
            Name = plugin.Name,
            Description = plugin.Description,
            Version = plugin.Version,
            Documentation = SafeExternalUrl(metadata?.Documentation),
            Source = SafeExternalUrl(metadata?.Source),
            Author = metadata?.Author,
            AuthorLink = SafeExternalUrl(metadata?.AuthorLink),
            Dependencies = CreateDependencyViewModels(plugin.Dependencies)
        };
    }

    private static string SafeExternalUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.AbsoluteUri
            : null;
    }

    private List<PluginDependencyViewModel> CreateDependencyViewModels(IEnumerable<IBTCPayServerPlugin.PluginDependency> dependencies)
    {
        return (dependencies ?? [])
            .Select(dependency => new PluginDependencyViewModel
            {
                Display = dependency.ToString(),
                IsMet = PluginManager.DependencyMet(dependency, pluginService.Installed)
            })
            .ToList();
    }

    private async Task<AvailablePlugin[]> LoadRemotePlugins()
    {
        try
        {
            return await pluginService.GetRemotePlugins(null);
        }
        catch (Exception ex)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = stringLocalizer["Remote plugins lookup failed. Try again later. Error: {0}", ex.Message].Value
            });
            return [];
        }
    }

    private IActionResult RedirectToPlugins(string selectedSlug)
    {
        return string.IsNullOrEmpty(selectedSlug)
            ? RedirectToAction(nameof(ListPlugins))
            : RedirectToAction(nameof(PluginDirectory), new { selectedSlug });
    }

    internal static string BuildDirectoryIframeUrl(
        Uri pluginSourceBaseUri,
        string btcpayVersion,
        bool includePreRelease)
    {
        if (pluginSourceBaseUri is null) return null;

        var baseUri = PluginBuilderClient.NormalizeBaseAddress(pluginSourceBaseUri);
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

        var baseUri = PluginBuilderClient.NormalizeBaseAddress(pluginSourceBaseUri);
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

    private sealed record PluginRuntimeState(
        IBTCPayServerPlugin[] VisibleLoadedPlugins,
        Dictionary<string, Version> DisabledVersions,
        (string command, string plugin)[] PendingCommands);
}
