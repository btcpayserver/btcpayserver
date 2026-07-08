using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Plugins;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using static BTCPayServer.Plugins.PluginService;

namespace BTCPayServer.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyServerSettings)]
[Route("server/plugins")]
public class UIPluginManagerController(
    PluginService pluginService,
    PoliciesSettings policiesSettings,
    IStringLocalizer stringLocalizer)
    : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    [HttpGet]
    public async Task<IActionResult> ListPlugins()
    {
        var remotePlugins = await LoadRemotePlugins(setErrorStatusMessage: true);
        var model = CreateInstalledPluginsViewModel(
            pluginService.LoadedPlugins,
            pluginService.Installed,
            remotePlugins,
            pluginService.GetPendingCommands(),
            pluginService.GetDisabledPlugins(),
            pluginService.GetVersionOfPendingInstall);
        return View("~/Views/UIServer/ListPlugins.cshtml", model);
    }

    [HttpGet("directory")]
    public async Task<IActionResult> PluginDirectory(string selectedSlug = null)
    {
        var remotePlugins = string.IsNullOrWhiteSpace(selectedSlug)
            ? []
            : await LoadRemotePlugins(setErrorStatusMessage: true);
        var model = CreatePluginDirectoryViewModel(
            selectedSlug,
            pluginService.LoadedPlugins,
            pluginService.Installed,
            remotePlugins,
            pluginService.GetPendingCommands(),
            pluginService.GetDisabledPlugins(),
            pluginService.GetVersionOfPendingInstall);
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
        return View("~/Views/UIServer/PluginDirectory.cshtml", model);
    }

    [HttpGet("panel")]
    public async Task<IActionResult> SelectedPluginPanel(string slug = null)
    {
        var remotePlugins = string.IsNullOrWhiteSpace(slug)
            ? []
            : await LoadRemotePlugins(setErrorStatusMessage: false);
        var model = CreateSelectedPluginPanelViewModel(
            slug,
            pluginService.LoadedPlugins,
            pluginService.Installed,
            remotePlugins,
            pluginService.GetPendingCommands(),
            pluginService.GetDisabledPlugins(),
            pluginService.GetVersionOfPendingInstall);
        model.EmbeddedDetailsUrl = BuildPluginDetailsEmbedUrl(
            pluginService.GetPluginSourceBaseUri(),
            model.SelectedSlug,
            model.Actions.FirstOrDefault(action => action.FormAction == nameof(InstallPlugin))?.Version,
            pluginService.GetShortBtcpayVersion(),
            policiesSettings.PluginPreReleases);
        return PartialView("~/Views/UIServer/_SelectedPluginPanel.cshtml", model);
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
            Message = StringLocalizer["Plugin scheduled to be uninstalled."].Value,
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
            Message = StringLocalizer["Plugin scheduled to be enabled."].Value,
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(ListPlugins));
    }

    [HttpPost("cancel")]
    public IActionResult CancelPluginCommands(
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

    [HttpPost("install")]
    public async Task<IActionResult> InstallPlugin(
        string plugin,
        string version = null,
        string selectedSlug = null,
        string returnTo = null)
    {
        var ctx = new DownloadPluginContext(plugin, version, new(), new(), null);
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
            Message = StringLocalizer["Files uploaded, restart server to load plugins"].Value,
            Severity = StatusMessageModel.StatusSeverity.Success
        });
        return RedirectToAction(nameof(ListPlugins));
    }

    private record DownloadPluginContext(string Plugin, string Version, Dictionary<string, AvailablePlugin> Downloaded, Dictionary<string, string> DependencyFailed, VersionCondition VersionCondition);

    private async Task DownloadPluginAndDependencies(DownloadPluginContext ctx)
    {
        if (ctx.Downloaded.ContainsKey(ctx.Plugin)
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
        ctx.Downloaded.Add(ctx.Plugin, manifest);
    }

    internal static InstalledPluginsViewModel CreateInstalledPluginsViewModel(
        IEnumerable<IBTCPayServerPlugin> loadedPlugins,
        Dictionary<string, Version> installedVersions,
        IEnumerable<AvailablePlugin> availablePlugins,
        (string command, string plugin)[] pendingCommands,
        Dictionary<string, Version> disabledVersions,
        Func<string, Version> getVersionOfPendingInstall)
    {
        var visibleLoadedPlugins = GetVisibleLoadedPlugins(loadedPlugins);
        var versionsByIdentifier = GetAvailableVersionsByIdentifier(availablePlugins);
        installedVersions ??= new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        disabledVersions ??= new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        pendingCommands ??= [];

        return new InstalledPluginsViewModel
        {
            DisabledPlugins = disabledVersions
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => CreateDisabledViewModel(
                    GetPluginState(pair.Key, visibleLoadedPlugins, installedVersions, versionsByIdentifier, pendingCommands, disabledVersions, getVersionOfPendingInstall),
                    installedVersions))
                .ToList(),
            InstalledPlugins = visibleLoadedPlugins
                .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .Select(plugin => CreateInstalledPluginCardViewModel(
                    GetPluginState(plugin.Identifier, visibleLoadedPlugins, installedVersions, versionsByIdentifier, pendingCommands, disabledVersions, getVersionOfPendingInstall),
                    installedVersions,
                    IsRequiredByOtherPlugins(plugin.Identifier, visibleLoadedPlugins, versionsByIdentifier, pendingCommands)))
                .ToList(),
            PendingActions = CreatePendingActions(pendingCommands)
        };
    }

    internal static PluginDirectoryViewModel CreatePluginDirectoryViewModel(
        string selectedSlug,
        IEnumerable<IBTCPayServerPlugin> loadedPlugins,
        Dictionary<string, Version> installedVersions,
        IEnumerable<AvailablePlugin> availablePlugins,
        (string command, string plugin)[] pendingCommands,
        Dictionary<string, Version> disabledVersions,
        Func<string, Version> getVersionOfPendingInstall)
    {
        var visibleLoadedPlugins = GetVisibleLoadedPlugins(loadedPlugins);
        disabledVersions ??= new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        pendingCommands ??= [];
        var selectedPanel = CreateSelectedPluginPanelViewModel(
            selectedSlug,
            visibleLoadedPlugins,
            installedVersions,
            availablePlugins,
            pendingCommands,
            disabledVersions,
            getVersionOfPendingInstall);

        return new PluginDirectoryViewModel
        {
            SelectedPluginSlug = selectedPanel.SelectedSlug,
            HiddenPluginIdentifiers = visibleLoadedPlugins
                .Select(plugin => plugin.Identifier)
                .Concat(disabledVersions.Keys)
                .Where(identifier => !string.IsNullOrEmpty(identifier))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(identifier => identifier, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PendingActions = CreatePendingActions(pendingCommands),
            SelectedPluginPanel = selectedPanel
        };
    }

    internal static PluginSelectedPanelViewModel CreateSelectedPluginPanelViewModel(
        string selectedSlug,
        IEnumerable<IBTCPayServerPlugin> loadedPlugins,
        Dictionary<string, Version> installedVersions,
        IEnumerable<AvailablePlugin> availablePlugins,
        (string command, string plugin)[] pendingCommands,
        Dictionary<string, Version> disabledVersions,
        Func<string, Version> getVersionOfPendingInstall)
    {
        if (string.IsNullOrEmpty(selectedSlug))
            return new PluginSelectedPanelViewModel { HasSelection = false };

        installedVersions ??= new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        pendingCommands ??= [];
        disabledVersions ??= new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        var visibleLoadedPlugins = GetVisibleLoadedPlugins(loadedPlugins);
        var remotePlugins = (availablePlugins ?? [])
            .Where(plugin => plugin is not null)
            .ToArray();
        var selectedAvailable = remotePlugins.FirstOrDefault(plugin =>
            plugin.CatalogSlug != null &&
            plugin.CatalogSlug.Equals(selectedSlug, StringComparison.OrdinalIgnoreCase));
        if (selectedAvailable is null)
            return new PluginSelectedPanelViewModel { HasSelection = false };

        var state = GetPluginState(
            selectedAvailable.Identifier,
            visibleLoadedPlugins,
            installedVersions,
            GetAvailableVersionsByIdentifier(remotePlugins),
            pendingCommands,
            disabledVersions,
            getVersionOfPendingInstall);
        PluginInfoViewModel plugin;
        if (state.DisabledVersion is not null)
        {
            plugin = CreatePluginInfo(state.BestUpdate ?? state.BestAvailable, installedVersions) ?? new PluginInfoViewModel
            {
                Identifier = state.Identifier,
                Name = state.Identifier,
                Version = state.DisabledVersion
            };
        }
        else if (state.LoadedPlugin is not null)
        {
            plugin = CreatePluginInfo(state.LoadedPlugin, state.BestAvailable, installedVersions);
        }
        else
        {
            plugin = CreatePluginInfo(state.BestAvailable, installedVersions);
        }

        return new PluginSelectedPanelViewModel
        {
            HasSelection = true,
            SelectedIdentifier = state.Identifier,
            SelectedSlug = plugin?.CatalogSlug,
            Plugin = plugin,
            InstalledVersion = state.LoadedPlugin?.Version,
            BestAvailableVersion = state.BestUpdate?.Version ?? state.BestAvailable?.Version,
            DisabledVersion = state.DisabledVersion,
            PendingAction = state.LastPendingAction,
            PendingVersion = state.PendingInstallVersion,
            Actions = CreateSelectedPanelActions(state, installedVersions)
        };
    }

    private static IBTCPayServerPlugin[] GetVisibleLoadedPlugins(IEnumerable<IBTCPayServerPlugin> loadedPlugins)
    {
        return (loadedPlugins ?? [])
            .Where(plugin => !plugin.SystemPlugin)
            .ToArray();
    }

    private static Dictionary<string, AvailablePlugin[]> GetAvailableVersionsByIdentifier(IEnumerable<AvailablePlugin> availablePlugins)
    {
        return (availablePlugins ?? [])
            .Where(plugin => plugin is not null)
            .GroupBy(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(plugin => plugin.Version).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static List<PendingPluginActionViewModel> CreatePendingActions((string command, string plugin)[] commands)
    {
        return (commands ?? [])
            .GroupBy(tuple => tuple.plugin, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PendingPluginActionViewModel
            {
                Plugin = group.Key,
                Action = group.Last().command
            })
            .OrderBy(action => action.Plugin, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PluginState GetPluginState(
        string identifier,
        IEnumerable<IBTCPayServerPlugin> loadedPlugins,
        Dictionary<string, Version> installedVersions,
        Dictionary<string, AvailablePlugin[]> availableVersionsByIdentifier,
        (string command, string plugin)[] pendingCommands,
        Dictionary<string, Version> disabledVersions,
        Func<string, Version> getVersionOfPendingInstall)
    {
        availableVersionsByIdentifier.TryGetValue(identifier, out var availableVersions);
        var bestAvailable = GetBestCandidate(availableVersions, installedVersions);

        var loadedPlugin = loadedPlugins.FirstOrDefault(plugin =>
            plugin.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
        disabledVersions.TryGetValue(identifier, out var disabledVersion);

        var currentVersion = loadedPlugin?.Version ?? disabledVersion;
        var updateCandidates = currentVersion is null
            ? []
            : availableVersions?
                .Where(plugin => plugin.Version > currentVersion)
                .ToArray() ?? [];
        var bestUpdate = GetBestCandidate(updateCandidates, installedVersions);

        var pluginCommands = pendingCommands
            .Where(tuple => tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var pendingInstallVersion = pluginCommands.Any(tuple => tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase))
            ? getVersionOfPendingInstall?.Invoke(identifier)
            : null;

        return new PluginState
        {
            Identifier = identifier,
            LoadedPlugin = loadedPlugin,
            DisabledVersion = disabledVersion,
            BestAvailable = bestAvailable,
            BestUpdate = bestUpdate,
            LastPendingAction = pluginCommands.LastOrDefault().command,
            HasPendingInstall = pluginCommands.Any(tuple => tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase)),
            HasPendingDelete = pluginCommands.Any(tuple => tuple.command.Equals("delete", StringComparison.OrdinalIgnoreCase)),
            HasPendingEnable = pluginCommands.Any(tuple => tuple.command.Equals("enable", StringComparison.OrdinalIgnoreCase)),
            PendingInstallVersion = pendingInstallVersion
        };
    }

    private static bool IsRequiredByOtherPlugins(
        string plugin,
        IEnumerable<IBTCPayServerPlugin> loadedPlugins,
        Dictionary<string, AvailablePlugin[]> availableVersionsByIdentifier,
        (string command, string plugin)[] pendingCommands)
    {
        var pendingDeletePlugins = pendingCommands
            .Where(tuple => tuple.command.Equals("delete", StringComparison.OrdinalIgnoreCase))
            .Select(tuple => tuple.plugin)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var installedPlugin in loadedPlugins)
        {
            if (pendingDeletePlugins.Contains(installedPlugin.Identifier))
            {
                continue;
            }

            if (installedPlugin.Dependencies.Any(dep => dep.Identifier.Equals(plugin, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        foreach (var pendingPlugin in pendingCommands
                     .Where(tuple =>
                         tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase) ||
                         tuple.command.Equals("enable", StringComparison.OrdinalIgnoreCase))
                     .Select(tuple => tuple.plugin)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (availableVersionsByIdentifier.TryGetValue(pendingPlugin, out var pendingAvailableVersions) &&
                pendingAvailableVersions.Any(availablePlugin =>
                    availablePlugin.Dependencies.Any(dep => dep.Identifier.Equals(plugin, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }
        }

        return false;
    }

    private static PluginDisabledViewModel CreateDisabledViewModel(PluginState state, Dictionary<string, Version> installed)
    {
        var actions = new List<PluginActionViewModel>();
        if (state.HasPendingInstall)
        {
            actions.Add(CreateDisabledAction("Marked for update", "btn btn-sm btn-outline-primary"));
        }
        else if (state.BestUpdate is not null && !state.HasPendingDelete && !state.HasPendingEnable)
        {
            var dependenciesMet = PluginManager.DependenciesMet(state.BestUpdate.Dependencies ?? [], installed);
            actions.Add(CreateInstallAction(
                state.Identifier,
                state.BestUpdate,
                "btn btn-sm btn-primary",
                dependenciesMet ? "Update" : "Schedule update",
                dependenciesMet ? null : "Schedule upgrade for when the dependencies have been met to ensure a smooth update"));
        }

        if (state.HasPendingEnable)
        {
            actions.Add(CreateDisabledAction("Marked for enabling", "btn btn-sm btn-outline-primary"));
        }
        else if (!state.HasPendingDelete && !state.HasPendingInstall)
        {
            actions.Add(CreatePostAction("EnablePlugin", "Enable", "btn btn-sm btn-outline-primary", state.Identifier));
        }

        if (state.HasPendingDelete)
        {
            actions.Add(CreateDisabledAction("Marked for deletion", "btn btn-sm btn-outline-danger"));
        }
        else if (!state.HasPendingEnable && !state.HasPendingInstall)
        {
            actions.Add(CreatePostAction("UnInstallPlugin", "Uninstall", "btn btn-sm btn-outline-danger", state.Identifier));
        }

        return new PluginDisabledViewModel
        {
            Identifier = state.Identifier,
            DisabledVersion = state.DisabledVersion,
            RecommendedUpdate = state.BestUpdate is not null
                ? CreatePluginInfo(state.BestUpdate, installed)
                : CreatePluginInfo(state.BestAvailable, installed),
            Actions = actions
        };
    }

    private static PluginInstalledCardViewModel CreateInstalledPluginCardViewModel(
        PluginState state,
        Dictionary<string, Version> installed,
        bool isRequiredByOtherPlugins)
    {
        var actions = new List<PluginActionViewModel>();
        if (!string.IsNullOrEmpty(state.LastPendingAction))
        {
            actions.Add(CreateCancelAction(state.Identifier, state.LastPendingAction, state.PendingInstallVersion, "btn btn-outline-secondary"));
        }
        else
        {
            if (state.BestUpdate is not null)
            {
                var dependenciesMet = PluginManager.DependenciesMet(state.BestUpdate.Dependencies ?? [], installed);
                actions.Add(CreateInstallAction(
                    state.Identifier,
                    state.BestUpdate,
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
                actions.Add(CreatePostAction("UnInstallPlugin", "Uninstall", "btn btn-outline-danger", state.Identifier));
            }
        }

        return new PluginInstalledCardViewModel
        {
            Current = CreatePluginInfo(state.LoadedPlugin, state.BestAvailable, installed),
            Update = CreatePluginInfo(state.BestUpdate, installed),
            PendingAction = state.LastPendingAction,
            Actions = actions
        };
    }

    private static List<PluginActionViewModel> CreateSelectedPanelActions(PluginState state, Dictionary<string, Version> installed)
    {
        var actions = new List<PluginActionViewModel>();
        if (!string.IsNullOrEmpty(state.LastPendingAction))
        {
            actions.Add(CreateCancelAction(
                state.Identifier,
                state.LastPendingAction,
                state.PendingInstallVersion,
                "btn btn-outline-secondary"));
        }
        else if (state.LoadedPlugin is null && state.DisabledVersion is null && state.BestAvailable is not null)
        {
            var dependenciesMet = PluginManager.DependenciesMet(state.BestAvailable.Dependencies ?? [], installed);
            actions.Add(CreateInstallAction(
                state.Identifier,
                state.BestAvailable,
                "btn btn-primary",
                dependenciesMet ? "Install" : "Schedule install",
                dependenciesMet ? null : "Schedule install for when the dependencies have been met to ensure a smooth update"));
        }

        return actions;
    }

    private static AvailablePlugin GetBestCandidate(IEnumerable<AvailablePlugin> plugins, Dictionary<string, Version> installed)
    {
        var ordered = plugins?
            .OrderByDescending(plugin => plugin.Version)
            .ToArray() ?? [];
        return ordered.FirstOrDefault(plugin => PluginManager.DependenciesMet(plugin.Dependencies ?? [], installed)) ?? ordered.FirstOrDefault();
    }

    private static PluginActionViewModel CreateInstallAction(
        string plugin,
        AvailablePlugin availablePlugin,
        string cssClass,
        string label,
        string tooltip = null)
    {
        return new PluginActionViewModel
        {
            FormAction = "InstallPlugin",
            Label = label,
            CssClass = cssClass,
            Plugin = plugin,
            Version = availablePlugin.Version?.ToString(),
            Tooltip = tooltip
        };
    }

    private static PluginActionViewModel CreatePostAction(string action, string label, string cssClass, string plugin)
    {
        return new PluginActionViewModel
        {
            FormAction = action,
            Label = label,
            CssClass = cssClass,
            Plugin = plugin
        };
    }

    private static PluginActionViewModel CreateCancelAction(string plugin, string pendingAction, Version pendingVersion, string cssClass)
    {
        var suffix = pendingVersion is null ? string.Empty : $" of {pendingVersion}";
        return new PluginActionViewModel
        {
            FormAction = "CancelPluginCommands",
            Label = $"Cancel pending {pendingAction}{suffix}",
            CssClass = cssClass,
            Plugin = plugin
        };
    }

    private static PluginActionViewModel CreateDisabledAction(string label, string cssClass, string tooltip = null)
    {
        return new PluginActionViewModel
        {
            Label = label,
            CssClass = cssClass,
            Tooltip = tooltip,
            Disabled = true
        };
    }

    private static PluginInfoViewModel CreatePluginInfo(AvailablePlugin plugin, Dictionary<string, Version> installed)
    {
        if (plugin is null)
        {
            return null;
        }

        return new PluginInfoViewModel
        {
            Identifier = plugin.Identifier,
            CatalogSlug = plugin.CatalogSlug,
            Name = plugin.Name,
            Description = plugin.Description,
            Version = plugin.Version,
            Documentation = SafeExternalUrl(plugin.Documentation),
            Source = SafeExternalUrl(plugin.Source),
            Author = plugin.Author,
            AuthorLink = SafeExternalUrl(plugin.AuthorLink),
            DependenciesMet = PluginManager.DependenciesMet(plugin.Dependencies ?? [], installed),
            Dependencies = CreateDependencyViewModels(plugin.Dependencies, installed)
        };
    }

    private static PluginInfoViewModel CreatePluginInfo(
        IBTCPayServerPlugin plugin,
        AvailablePlugin metadata,
        Dictionary<string, Version> installed)
    {
        if (plugin is null) return null;

        return new PluginInfoViewModel
        {
            Identifier = plugin.Identifier,
            CatalogSlug = metadata?.CatalogSlug,
            Name = plugin.Name,
            Description = plugin.Description,
            Version = plugin.Version,
            Documentation = SafeExternalUrl(metadata?.Documentation),
            Source = SafeExternalUrl(metadata?.Source),
            Author = metadata?.Author,
            AuthorLink = SafeExternalUrl(metadata?.AuthorLink),
            DependenciesMet = PluginManager.DependenciesMet(plugin.Dependencies ?? [], installed),
            Dependencies = CreateDependencyViewModels(plugin.Dependencies, installed)
        };
    }

    private static string SafeExternalUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.AbsoluteUri
            : null;
    }

    private static List<PluginDependencyViewModel> CreateDependencyViewModels(IEnumerable<IBTCPayServerPlugin.PluginDependency> dependencies, Dictionary<string, Version> installed)
    {
        return (dependencies ?? [])
            .Select(dependency => new PluginDependencyViewModel
            {
                Display = dependency.ToString(),
                IsMet = PluginManager.DependencyMet(dependency, installed)
            })
            .ToList();
    }

    private async Task<AvailablePlugin[]> LoadRemotePlugins(bool setErrorStatusMessage)
    {
        try
        {
            return await pluginService.GetRemotePlugins(null);
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
            return [];
        }
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

    private sealed class PluginState
    {
        public string Identifier { get; init; }
        public IBTCPayServerPlugin LoadedPlugin { get; init; }
        public Version DisabledVersion { get; init; }
        public AvailablePlugin BestAvailable { get; init; }
        public AvailablePlugin BestUpdate { get; init; }
        public string LastPendingAction { get; init; }
        public bool HasPendingInstall { get; init; }
        public bool HasPendingDelete { get; init; }
        public bool HasPendingEnable { get; init; }
        public Version PendingInstallVersion { get; init; }
    }
}
