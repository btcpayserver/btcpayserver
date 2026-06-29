using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Models.ServerViewModels;

namespace BTCPayServer.Plugins
{
    public class PluginManagementProjectionService
    {
        public class ProjectionSource
        {
            public IEnumerable<IBTCPayServerPlugin> LoadedPlugins { get; set; } = [];
            public Dictionary<string, Version> Installed { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public IEnumerable<PluginService.AvailablePlugin> AllAvailable { get; set; } = [];
            public (string command, string plugin)[] Commands { get; set; } = [];
            public Dictionary<string, Version> Disabled { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Func<string, Version> GetVersionOfPendingInstall { get; set; }
            public string SelectedPluginIdentifier { get; set; }
            public string SelectedPluginSlug { get; set; }
        }

        public InstalledPluginsViewModel CreateInstalledPluginsViewModel(ProjectionSource source)
        {
            source ??= new ProjectionSource();
            var data = CreatePluginsStateData(source);
            return new InstalledPluginsViewModel
            {
                DisabledPlugins = data.Disabled
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => CreateDisabledViewModel(BuildState(pair.Key, data), data.Installed))
                    .ToList(),
                InstalledPlugins = data.LoadedPlugins
                    .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(plugin => CreateInstalledPluginCardViewModel(BuildState(plugin.Identifier, data), data.Installed))
                    .ToList(),
                PendingActions = CreatePendingActions(data.Commands)
            };
        }

        public PluginDirectoryViewModel CreatePluginDirectoryViewModel(ProjectionSource source)
        {
            source ??= new ProjectionSource();
            var data = CreatePluginsStateData(source);
            var selected = CreateSelectedPanel(source, data);
            return new PluginDirectoryViewModel
            {
                SelectedPluginIdentifier = selected.SelectedIdentifier,
                SelectedPluginSlug = selected.SelectedSlug,
                HiddenPluginIdentifiers = CreateHiddenPluginIdentifiers(data),
                PendingActions = CreatePendingActions(data.Commands),
                SelectedPluginPanel = selected.Panel
            };
        }

        public PluginSelectedPanelViewModel CreateSelectedPluginPanelViewModel(ProjectionSource source)
        {
            source ??= new ProjectionSource();
            var data = CreatePluginsStateData(source);
            return CreateSelectedPanel(source, data).Panel;
        }

        private static PluginsStateData CreatePluginsStateData(ProjectionSource source)
        {
            var loadedPlugins = (source.LoadedPlugins ?? []).Where(plugin => !plugin.SystemPlugin).ToArray();
            var installed = source.Installed ?? new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            var commands = source.Commands ?? [];
            var disabled = source.Disabled ?? new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            var allAvailable = (source.AllAvailable ?? [])
                .Where(plugin => plugin is not null)
                .ToArray();
            var availableVersionsByIdentifier = allAvailable
                .GroupBy(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(plugin => plugin.Version).ToArray(), StringComparer.OrdinalIgnoreCase);
            var pendingInstallVersions = commands
                .Where(tuple => tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase))
                .Select(tuple => tuple.plugin)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    plugin => plugin,
                    plugin => source.GetVersionOfPendingInstall?.Invoke(plugin),
                    StringComparer.OrdinalIgnoreCase);

            return new PluginsStateData(
                loadedPlugins,
                installed,
                commands,
                disabled,
                allAvailable,
                availableVersionsByIdentifier,
                pendingInstallVersions);
        }

        private (string SelectedIdentifier, string SelectedSlug, PluginSelectedPanelViewModel Panel) CreateSelectedPanel(
            ProjectionSource source,
            PluginsStateData data)
        {
            var selectedState = ResolveSelectedState(
                source.SelectedPluginIdentifier,
                source.SelectedPluginSlug,
                data.LoadedPlugins,
                data.Disabled,
                data.AllAvailable);
            if (selectedState is null)
            {
                return (null, null, new PluginSelectedPanelViewModel { HasSelection = false });
            }

            var state = BuildState(selectedState.Identifier, data);
            var plugin = CreatePanelInfo(state, data.Installed);
            var actions = CreatePanelActions(state, data.Installed);
            var (currentState, description) = GetPanelStateText(state, plugin);

            var panel = new PluginSelectedPanelViewModel
            {
                HasSelection = true,
                SelectedIdentifier = state.Identifier,
                SelectedSlug = plugin?.CatalogSlug,
                Plugin = plugin,
                CurrentState = currentState,
                StateDescription = description,
                InstalledVersion = state.LoadedPlugin?.Version,
                BestAvailableVersion = state.BestUpdate?.Version ?? state.BestAvailable?.Version,
                DisabledVersion = state.DisabledVersion,
                PendingAction = state.LastPendingAction,
                PendingVersion = state.PendingInstallVersion,
                Actions = actions
            };
            return (panel.SelectedIdentifier, panel.SelectedSlug, panel);
        }

        private static List<PendingPluginActionViewModel> CreatePendingActions((string command, string plugin)[] commands)
        {
            return commands
                .GroupBy(tuple => tuple.plugin, StringComparer.OrdinalIgnoreCase)
                .Select(group => new PendingPluginActionViewModel
                {
                    Plugin = group.Key,
                    Action = group.Last().command
                })
                .OrderBy(action => action.Plugin, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string[] CreateHiddenPluginIdentifiers(PluginsStateData data)
        {
            return data.LoadedPlugins
                .Select(plugin => plugin.Identifier)
                .Concat(data.Disabled.Keys)
                .Where(identifier => !string.IsNullOrEmpty(identifier))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(identifier => identifier, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static PluginState BuildState(string identifier, PluginsStateData data)
        {
            data.AvailableVersionsByIdentifier.TryGetValue(identifier, out var availableVersions);
            var bestAvailable = GetBestCandidate(availableVersions, data.Installed) ?? availableVersions?.FirstOrDefault();

            var loadedPlugin = data.LoadedPlugins.FirstOrDefault(plugin =>
                plugin.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            data.Disabled.TryGetValue(identifier, out var disabledVersion);

            var installedVersion = loadedPlugin?.Version;
            var currentVersion = installedVersion ?? disabledVersion;
            var updateCandidates = currentVersion is null
                ? []
                : availableVersions?
                    .Where(plugin => plugin.Version > currentVersion)
                    .ToArray() ?? [];

            var bestUpdate = GetBestCandidate(updateCandidates, data.Installed);

            data.PendingInstallVersions.TryGetValue(identifier, out var pendingVersion);

            return new PluginState
            {
                Identifier = identifier,
                LoadedPlugin = loadedPlugin,
                DisabledVersion = disabledVersion,
                BestAvailable = bestAvailable,
                BestUpdate = bestUpdate,
                IsRequiredByOtherPlugins = HasPluginsDependingOn(identifier, data),
                LastPendingAction = data.Commands.LastOrDefault(tuple => tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase)).command,
                HasPendingInstall = data.Commands.Any(tuple =>
                    tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase) &&
                    tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase)),
                HasPendingDelete = data.Commands.Any(tuple =>
                    tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase) &&
                    tuple.command.Equals("delete", StringComparison.OrdinalIgnoreCase)),
                HasPendingEnable = data.Commands.Any(tuple =>
                    tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase) &&
                    tuple.command.Equals("enable", StringComparison.OrdinalIgnoreCase)),
                PendingInstallVersion = pendingVersion
            };
        }

        private static bool HasPluginsDependingOn(string plugin, PluginsStateData data)
        {
            var pendingDeletePlugins = data.Commands
                .Where(tuple => tuple.command.Equals("delete", StringComparison.OrdinalIgnoreCase))
                .Select(tuple => tuple.plugin)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var installedPlugin in data.LoadedPlugins)
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

            foreach (var pendingPlugin in data.Commands
                         .Where(tuple =>
                             tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase) ||
                             tuple.command.Equals("enable", StringComparison.OrdinalIgnoreCase))
                         .Select(tuple => tuple.plugin)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (data.AvailableVersionsByIdentifier.TryGetValue(pendingPlugin, out var pendingAvailableVersions) &&
                    pendingAvailableVersions.Any(availablePlugin =>
                        availablePlugin.Dependencies.Any(dep => dep.Identifier.Equals(plugin, StringComparison.OrdinalIgnoreCase))))
                {
                    return true;
                }
            }

            return false;
        }

        private PluginDisabledViewModel CreateDisabledViewModel(PluginState state, Dictionary<string, Version> installed)
        {
            var actions = new List<PluginActionViewModel>();
            if (state.HasPendingInstall)
            {
                actions.Add(CreateDisabledAction("Marked for update", "btn btn-sm btn-outline-primary"));
            }
            else if (state.BestUpdate is not null && !state.HasPendingDelete && !state.HasPendingEnable)
            {
                var dependenciesMet = DependenciesMet(state.BestUpdate.Dependencies, installed);
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
                    ? CreateInfo(state.BestUpdate, installed)
                    : CreateInfo(state.BestAvailable, installed),
                Actions = actions
            };
        }

        private PluginInstalledCardViewModel CreateInstalledPluginCardViewModel(PluginState state, Dictionary<string, Version> installed)
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
                    var dependenciesMet = DependenciesMet(state.BestUpdate.Dependencies, installed);
                    actions.Add(CreateInstallAction(
                        state.Identifier,
                        state.BestUpdate,
                        "btn btn-secondary",
                        dependenciesMet ? "Update" : "Schedule update",
                        dependenciesMet ? null : "Schedule upgrade for when the dependencies have been met to ensure a smooth update"));
                }

                if (state.IsRequiredByOtherPlugins)
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
                Current = CreateInfo(state.LoadedPlugin, state.BestAvailable, installed),
                Update = CreateInfo(state.BestUpdate, installed),
                PendingAction = state.LastPendingAction,
                Actions = actions
            };
        }

        private List<PluginActionViewModel> CreatePanelActions(PluginState state, Dictionary<string, Version> installed)
        {
            var actions = new List<PluginActionViewModel>();
            if (!string.IsNullOrEmpty(state.LastPendingAction))
            {
                actions.Add(CreateCancelAction(state.Identifier, state.LastPendingAction, state.PendingInstallVersion, "btn btn-outline-secondary"));
                return actions;
            }

            if (state.LoadedPlugin is null && state.DisabledVersion is null && state.BestAvailable is not null)
            {
                var dependenciesMet = DependenciesMet(state.BestAvailable.Dependencies, installed);
                actions.Add(CreateInstallAction(
                    state.Identifier,
                    state.BestAvailable,
                    "btn btn-primary",
                    dependenciesMet ? "Install" : "Schedule install",
                    dependenciesMet ? null : "Schedule install for when the dependencies have been met to ensure a smooth update"));
            }

            return actions;
        }

        private static (string CurrentState, string Description) GetPanelStateText(PluginState state, PluginInfoViewModel plugin)
        {
            if (!string.IsNullOrEmpty(state.LastPendingAction))
            {
                var suffix = state.PendingInstallVersion is null ? string.Empty : $" {state.PendingInstallVersion}";
                return ("Pending action", $"BTCPay Server has queued {state.LastPendingAction}{suffix} for this plugin.");
            }

            if (state.DisabledVersion is not null)
            {
                return ("Disabled", $"Version {state.DisabledVersion} is disabled on this server.");
            }

            if (state.LoadedPlugin is not null)
            {
                return state.BestUpdate is not null
                    ? ("Installed", $"Version {state.BestUpdate.Version} is available. Manage updates from Installed Plugins.")
                    : ("Installed", "This plugin is already installed on the server.");
            }

            if (plugin is not null && !plugin.DependenciesMet)
            {
                return ("Not installed", "Dependencies are not met yet, so BTCPay Server will schedule the install.");
            }

            return ("Not installed", null);
        }

        private PluginInfoViewModel CreatePanelInfo(PluginState state, Dictionary<string, Version> installed)
        {
            if (state.DisabledVersion is not null)
            {
                return CreateInfo(state.BestUpdate ?? state.BestAvailable, installed) ?? new PluginInfoViewModel
                {
                    Identifier = state.Identifier,
                    Name = state.Identifier,
                    Version = state.DisabledVersion
                };
            }

            if (state.LoadedPlugin is not null)
            {
                return CreateInfo(state.LoadedPlugin, state.BestAvailable, installed);
            }

            return CreateInfo(state.BestAvailable, installed);
        }

        private static SelectedState ResolveSelectedState(
            string selectedIdentifier,
            string selectedSlug,
            IEnumerable<IBTCPayServerPlugin> loadedPlugins,
            Dictionary<string, Version> disabled,
            IEnumerable<PluginService.AvailablePlugin> allAvailable)
        {
            if (!string.IsNullOrEmpty(selectedSlug))
            {
                var available = allAvailable.FirstOrDefault(plugin =>
                    plugin.CatalogSlug != null &&
                    plugin.CatalogSlug.Equals(selectedSlug, StringComparison.OrdinalIgnoreCase));
                if (available is not null)
                {
                    return new SelectedState(available.Identifier, available.CatalogSlug);
                }
            }

            if (!string.IsNullOrEmpty(selectedIdentifier))
            {
                if (loadedPlugins.Any(plugin => plugin.Identifier.Equals(selectedIdentifier, StringComparison.OrdinalIgnoreCase)) ||
                    disabled.ContainsKey(selectedIdentifier) ||
                    allAvailable.Any(plugin => plugin.Identifier.Equals(selectedIdentifier, StringComparison.OrdinalIgnoreCase)))
                {
                    var resolvedSlug = allAvailable.FirstOrDefault(plugin =>
                        plugin.Identifier.Equals(selectedIdentifier, StringComparison.OrdinalIgnoreCase))?.CatalogSlug;
                    return new SelectedState(selectedIdentifier, resolvedSlug);
                }
            }

            return null;
        }

        private static PluginService.AvailablePlugin GetBestCandidate(IEnumerable<PluginService.AvailablePlugin> plugins, Dictionary<string, Version> installed)
        {
            var ordered = plugins?
                .OrderByDescending(plugin => plugin.Version)
                .ToArray() ?? [];
            return ordered.FirstOrDefault(plugin => DependenciesMet(plugin.Dependencies, installed)) ?? ordered.FirstOrDefault();
        }

        private static PluginActionViewModel CreateInstallAction(
            string plugin,
            PluginService.AvailablePlugin availablePlugin,
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

        private static PluginInfoViewModel CreateInfo(PluginService.AvailablePlugin plugin, Dictionary<string, Version> installed)
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
                DependenciesMet = DependenciesMet(plugin.Dependencies, installed),
                Dependencies = CreateDependencies(plugin.Dependencies, installed)
            };
        }

        private static PluginInfoViewModel CreateInfo(
            IBTCPayServerPlugin plugin,
            PluginService.AvailablePlugin metadata,
            Dictionary<string, Version> installed)
        {
            if (plugin is null)
            {
                return null;
            }

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
                DependenciesMet = DependenciesMet(plugin.Dependencies, installed),
                Dependencies = CreateDependencies(plugin.Dependencies, installed)
            };
        }

        private static string SafeExternalUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.AbsoluteUri
                : null;
        }

        private static bool DependenciesMet(IEnumerable<IBTCPayServerPlugin.PluginDependency> dependencies, Dictionary<string, Version> installed)
        {
            installed ??= new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            return PluginManager.DependenciesMet(dependencies ?? [], installed);
        }

        private static List<PluginDependencyViewModel> CreateDependencies(IEnumerable<IBTCPayServerPlugin.PluginDependency> dependencies, Dictionary<string, Version> installed)
        {
            installed ??= new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            return (dependencies ?? [])
                .Select(dependency => new PluginDependencyViewModel
                {
                    Display = dependency.ToString(),
                    IsMet = PluginManager.DependencyMet(dependency, installed)
                })
                .ToList();
        }

        private sealed class PluginState
        {
            public string Identifier { get; init; }
            public IBTCPayServerPlugin LoadedPlugin { get; init; }
            public Version DisabledVersion { get; init; }
            public PluginService.AvailablePlugin BestAvailable { get; init; }
            public PluginService.AvailablePlugin BestUpdate { get; init; }
            public bool IsRequiredByOtherPlugins { get; init; }
            public string LastPendingAction { get; init; }
            public bool HasPendingInstall { get; init; }
            public bool HasPendingDelete { get; init; }
            public bool HasPendingEnable { get; init; }
            public Version PendingInstallVersion { get; init; }
        }

        private sealed record PluginsStateData(
            IBTCPayServerPlugin[] LoadedPlugins,
            Dictionary<string, Version> Installed,
            (string command, string plugin)[] Commands,
            Dictionary<string, Version> Disabled,
            PluginService.AvailablePlugin[] AllAvailable,
            Dictionary<string, PluginService.AvailablePlugin[]> AvailableVersionsByIdentifier,
            Dictionary<string, Version> PendingInstallVersions);

        private sealed record SelectedState(string Identifier, string ResolvedSlug);
    }
}
