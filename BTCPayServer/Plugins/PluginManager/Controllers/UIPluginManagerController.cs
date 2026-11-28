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
        var selectedPlugin = string.IsNullOrWhiteSpace(selectedSlug)
            ? null
            : await LoadSelectedPlugin(selectedSlug);
        var model = CreatePluginDirectoryViewModel(selectedPlugin);
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

        AvailablePlugin selectedPlugin;
        try
        {
            selectedPlugin = await pluginService.GetDirectoryPluginBySlug(slug);
        }
        catch
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var model = CreateSelectedPluginPanelViewModel(selectedPlugin, GetPluginRuntimeState());
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
        var error = await DownloadPluginAndDependencies(plugin, version);
        if (error is null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = stringLocalizer["Plugin scheduled to be installed."].Value,
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else
        {
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

    // The status message lives in a cookie, so a crafted upload can't make it grow without bound
    private const int MaxRejectedNameLength = 300;

    [HttpPost("upload")]
    public async Task<IActionResult> UploadPlugin(IFormFile file)
    {
        if (file is null)
            return BadRequest();

        var plugin = file.FileName.TrimEnd(PluginManager.BTCPayPluginSuffix, StringComparison.InvariantCultureIgnoreCase);
        var status = new StatusMessageModel
        {
            Message = stringLocalizer["File uploaded, restart server to load plugin"].Value,
            Severity = StatusMessageModel.StatusSeverity.Success
        };
        if (file.Length == 0 || !file.FileName.IsValidFileName() || !pluginService.IsSafePluginName(plugin))
        {
            var name = file.FileName;
            if (name.Length > MaxRejectedNameLength)
                name = name[..MaxRejectedNameLength] + "...";
            status.Message = stringLocalizer["Not uploaded due to an invalid plugin file: {0}", name].Value;
            status.Severity = StatusMessageModel.StatusSeverity.Warning;
        }
        else
        {
            await pluginService.UploadPlugin(file);
            pluginService.InstallPlugin(plugin);
        }
        TempData.SetStatusMessageModel(status);
        return RedirectToAction(nameof(ListPlugins));
    }

    internal async Task<string> DownloadPluginAndDependencies(string plugin, string version)
    {
        var selected = new Dictionary<string, AvailablePlugin>(StringComparer.OrdinalIgnoreCase);
        var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await ResolvePlugin(plugin, version, null);
            var installOrder = GetPluginInstallOrder(selected);
            foreach (var candidate in installOrder)
                await pluginService.DownloadResolvedPlugin(candidate.Identifier, candidate);
            foreach (var candidate in installOrder)
                pluginService.InstallPlugin(candidate.Identifier);
            return null;
        }
        catch (Exception ex)
        {
            return $"{plugin}: {ex.Message}";
        }

        async Task ResolvePlugin(string identifier, string requestedVersion, VersionCondition condition)
        {
            if (selected.TryGetValue(identifier, out var resolved))
            {
                if (condition?.IsFulfilled(resolved.Version) == false)
                    throw new InvalidOperationException($"The selected version of {identifier} does not satisfy {condition}.");
                return;
            }

            if (!resolving.Add(identifier))
                throw new InvalidOperationException($"Plugin dependency cycle involving {identifier}.");

            var manifest = await pluginService.ResolveRemotePlugin(identifier, requestedVersion, condition);
            foreach (var dep in manifest.Dependencies ?? [])
            {
                if (PluginManager.DependencyMet(dep, pluginService.Installed))
                    continue;

                if (dep.Identifier.Equals("BTCPayServer", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"This condition can't be satisfied {dep}");

                var dependencyCondition = dep.ParseCondition();
                if (dependencyCondition is VersionCondition.Not)
                    throw new InvalidOperationException($"The currently installed plugin {dep.Identifier} is incompatible with this plugin.");

                await ResolvePlugin(dep.Identifier, null, dependencyCondition);
            }
            resolving.Remove(identifier);
            selected.Add(identifier, manifest);
        }
    }

    private AvailablePlugin[] GetPluginInstallOrder(Dictionary<string, AvailablePlugin> selected)
    {
        var projected = pluginService.LoadedPlugins.ToDictionary(
            plugin => plugin.Identifier, plugin => (plugin.Version, plugin.Dependencies), StringComparer.OrdinalIgnoreCase);
        foreach (var (command, identifier) in pluginService.GetPendingCommands())
        {
            if (command is "delete" or "disable")
                projected.Remove(identifier);
            else if (pluginService.GetPendingPluginManifest(command, identifier) is { } pending)
                projected[identifier] = (pending.Version, pending.Dependencies);
        }
        foreach (var (identifier, candidate) in selected)
            projected[identifier] = (candidate.Version, candidate.Dependencies);

        var versions = projected.ToDictionary(pair => pair.Key, pair => pair.Value.Version, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in selected.Values)
        {
            if (!PluginManager.DependenciesMet(candidate.Dependencies ?? [], versions))
                throw new InvalidOperationException($"The selected plugin versions do not meet the dependencies of {candidate.Identifier}.");
        }

        return projected.Keys.Select(identifier => identifier.ToUpperInvariant()).ToArray()
            .TopologicalSort(identifier => (projected[identifier].Dependencies ?? []).Select(dep => dep.Identifier.ToUpperInvariant()))
            .Where(selected.ContainsKey)
            .Select(identifier => selected[identifier])
            .ToArray();
    }

    internal async Task<InstalledPluginsViewModel> CreateInstalledPluginsViewModel(
        IEnumerable<AvailablePlugin> remotePlugins = null)
    {
        var runtimeState = GetPluginRuntimeState();
        var pluginFailures = pluginService.GetPluginFailures();
        remotePlugins ??= await LoadLatestVersionsForInstalledPlugins(runtimeState);
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
                    runtimeState.PendingCommands,
                    pluginFailures.GetValueOrDefault(pair.Key)))
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

    internal PluginDirectoryViewModel CreatePluginDirectoryViewModel(AvailablePlugin selectedPlugin)
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
            SelectedPluginPanel = CreateSelectedPluginPanelViewModel(selectedPlugin, runtimeState)
        };
    }

    private PluginSelectedPanelViewModel CreateSelectedPluginPanelViewModel(
        AvailablePlugin selectedPlugin,
        PluginRuntimeState runtimeState)
    {
        if (selectedPlugin is null)
            return new PluginSelectedPanelViewModel();

        var identifier = selectedPlugin.Identifier;
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
            SelectedSlug = selectedPlugin.CatalogSlug,
            PluginIdentifier = identifier,
            PluginName = selectedPlugin.Name,
            InstalledVersion = loadedPlugin?.Version,
            DisabledVersion = disabledVersion,
            PendingAction = pendingAction,
            PendingVersion = pendingVersion,
            InstallVersion = canInstall
                ? selectedPlugin.Version?.ToString()
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
        (string command, string plugin)[] pendingCommands,
        PluginFailureDiagnostic failure)
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
            Failure = failure,
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

    private async Task<AvailablePlugin[]> LoadLatestVersionsForInstalledPlugins(PluginRuntimeState runtimeState)
    {
        try
        {
            var pendingPluginIdentifiers = runtimeState.PendingCommands
                .Where(command =>
                    command.command.Equals("install", StringComparison.OrdinalIgnoreCase) ||
                    command.command.Equals("enable", StringComparison.OrdinalIgnoreCase))
                .Select(command => command.plugin);
            return await pluginService.GetLatestVersionsForInstalledPlugins(
                runtimeState.DisabledVersions,
                pendingPluginIdentifiers);
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

    private async Task<AvailablePlugin> LoadSelectedPlugin(string slug)
    {
        try
        {
            return await pluginService.GetDirectoryPluginBySlug(slug);
        }
        catch (Exception ex)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = stringLocalizer["Remote plugins lookup failed. Try again later. Error: {0}", ex.Message].Value
            });
            return null;
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
