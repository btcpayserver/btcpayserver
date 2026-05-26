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
            public IEnumerable<PluginService.AvailablePlugin> AvailableForListing { get; set; } = [];
            public (string command, string plugin)[] Commands { get; set; } = [];
            public Dictionary<string, Version> Disabled { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public IEnumerable<string> RecommendedPluginIdentifiers { get; set; } = [];
            public Func<string, Version> GetVersionOfPendingInstall { get; set; }
            public string SelectedPluginIdentifier { get; set; }
            public string SelectedPluginSlug { get; set; }
            public string Search { get; set; }
        }

        public ManagePluginsShellViewModel CreateViewModel(ProjectionSource source)
        {
            var loadedPlugins = source.LoadedPlugins.Where(plugin => !plugin.SystemPlugin).ToArray();
            var installed = NormalizeVersions(source.Installed);
            var commands = source.Commands ?? [];
            var disabled = NormalizeVersions(source.Disabled);
            var recommendedIds = new HashSet<string>(
                source.RecommendedPluginIdentifiers ?? [],
                StringComparer.OrdinalIgnoreCase);

            var allAvailable = (source.AllAvailable ?? [])
                .Where(plugin => plugin is not null)
                .ToArray();
            var availableForListing = (source.AvailableForListing ?? [])
                .Where(plugin => plugin is not null)
                .ToArray();

            var availableGroups = allAvailable
                .GroupBy(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(plugin => plugin.Version).ToArray(), StringComparer.OrdinalIgnoreCase);

            var listingCandidates = availableForListing
                .Where(plugin => !installed.ContainsKey(plugin.Identifier) && !disabled.ContainsKey(plugin.Identifier))
                .GroupBy(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase)
                .Select(group => GetBestCandidate(group, installed) ?? group.OrderByDescending(plugin => plugin.Version).First())
                .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var pendingInstallVersions = commands
                .Where(tuple => tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase))
                .Select(tuple => tuple.plugin)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    plugin => plugin,
                    plugin => source.GetVersionOfPendingInstall?.Invoke(plugin),
                    StringComparer.OrdinalIgnoreCase);

            var pendingDeletePlugins = commands
                .Where(tuple => tuple.command.Equals("delete", StringComparison.OrdinalIgnoreCase))
                .Select(tuple => tuple.plugin)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var pendingDependencyPlugins = commands
                .Where(tuple =>
                    tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase) ||
                    tuple.command.Equals("enable", StringComparison.OrdinalIgnoreCase))
                .Select(tuple => tuple.plugin)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            bool DependentOn(string plugin)
            {
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

                foreach (var pendingPlugin in pendingDependencyPlugins)
                {
                    if (availableGroups.TryGetValue(pendingPlugin, out var pendingAvailableVersions) &&
                        pendingAvailableVersions.Any(availablePlugin =>
                            availablePlugin.Dependencies.Any(dep => dep.Identifier.Equals(plugin, StringComparison.OrdinalIgnoreCase))))
                    {
                        return true;
                    }
                }

                return false;
            }

            PluginState BuildState(string identifier)
            {
                availableGroups.TryGetValue(identifier, out var availableVersions);
                var bestAvailable = GetBestCandidate(availableVersions, installed) ?? availableVersions?.FirstOrDefault();

                var loadedPlugin = loadedPlugins.FirstOrDefault(plugin =>
                    plugin.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
                disabled.TryGetValue(identifier, out var disabledVersion);

                var installedVersion = loadedPlugin?.Version;
                var currentVersion = installedVersion ?? disabledVersion;
                var updateCandidates = currentVersion is null
                    ? []
                    : availableVersions?
                        .Where(plugin => plugin.Version > currentVersion)
                        .ToArray() ?? [];

                var bestUpdate = GetBestCandidate(updateCandidates, installed);

                pendingInstallVersions.TryGetValue(identifier, out var pendingVersion);

                return new PluginState
                {
                    Identifier = identifier,
                    LoadedPlugin = loadedPlugin,
                    DisabledVersion = disabledVersion,
                    BestAvailable = bestAvailable,
                    BestUpdate = bestUpdate,
                    IsDependentOn = DependentOn(identifier),
                    Recommended = recommendedIds.Contains(identifier),
                    LastPendingAction = commands.LastOrDefault(tuple => tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase)).command,
                    HasPendingInstall = commands.Any(tuple =>
                        tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase) &&
                        tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase)),
                    HasPendingDelete = commands.Any(tuple =>
                        tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase) &&
                        tuple.command.Equals("delete", StringComparison.OrdinalIgnoreCase)),
                    HasPendingEnable = commands.Any(tuple =>
                        tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase) &&
                        tuple.command.Equals("enable", StringComparison.OrdinalIgnoreCase)),
                    PendingInstallVersion = pendingVersion
                };
            }

            var states = new Dictionary<string, PluginState>(StringComparer.OrdinalIgnoreCase);
            PluginState GetState(string identifier)
            {
                if (!states.TryGetValue(identifier, out var state))
                {
                    state = BuildState(identifier);
                    states[identifier] = state;
                }

                return state;
            }

            var model = new ManagePluginsShellViewModel
            {
                Search = source.Search,
                DisabledPlugins = disabled
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => CreateDisabledViewModel(GetState(pair.Key), installed))
                    .ToList(),
                InstalledPlugins = loadedPlugins
                    .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(plugin => CreateInstalledViewModel(GetState(plugin.Identifier), installed))
                    .ToList(),
                AvailablePlugins = listingCandidates
                    .Select(plugin => CreateAvailableViewModel(GetState(plugin.Identifier), plugin, installed))
                    .ToList(),
                PendingActions = commands
                    .GroupBy(tuple => tuple.plugin, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new PendingPluginActionViewModel
                    {
                        Plugin = group.Key,
                        Action = group.Last().command
                    })
                    .OrderBy(action => action.Plugin, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            var selectedState = ResolveSelectedState(source.SelectedPluginIdentifier, source.SelectedPluginSlug, GetState, loadedPlugins, disabled, allAvailable);
            model.SelectedPluginIdentifier = selectedState?.Identifier ?? source.SelectedPluginIdentifier;
            model.SelectedPluginSlug = selectedState?.ResolvedSlug ?? source.SelectedPluginSlug;
            model.SelectedPluginPanel = selectedState?.State is null && !string.IsNullOrEmpty(model.SelectedPluginSlug)
                ? CreateDirectoryOnlyPanelViewModel(model.SelectedPluginIdentifier, model.SelectedPluginSlug)
                : CreateSelectedPanelViewModel(selectedState?.State, installed);
            return model;
        }

        private static PluginSelectedPanelViewModel CreateDirectoryOnlyPanelViewModel(
            string selectedIdentifier,
            string selectedSlug)
        {
            return new PluginSelectedPanelViewModel
            {
                HasSelection = true,
                SelectedIdentifier = selectedIdentifier,
                SelectedSlug = selectedSlug,
                Plugin = new PluginInfoViewModel
                {
                    Identifier = string.IsNullOrEmpty(selectedIdentifier) ? selectedSlug : selectedIdentifier,
                    CatalogSlug = selectedSlug,
                    Name = selectedSlug
                }
            };
        }

        private PluginSelectedPanelViewModel CreateSelectedPanelViewModel(
            PluginState state,
            Dictionary<string, Version> installed)
        {
            if (state is null)
            {
                return new PluginSelectedPanelViewModel { HasSelection = false };
            }

            var plugin = CreatePanelInfo(state, installed);
            var actions = CreatePanelActions(state, installed);
            var (currentState, description) = GetPanelStateText(state, plugin);

            return new PluginSelectedPanelViewModel
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

        private PluginInstalledCardViewModel CreateInstalledViewModel(PluginState state, Dictionary<string, Version> installed)
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

                if (state.IsDependentOn)
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

        private PluginAvailableCardViewModel CreateAvailableViewModel(
            PluginState state,
            PluginService.AvailablePlugin listingPlugin,
            Dictionary<string, Version> installed)
        {
            var actions = new List<PluginActionViewModel>();
            var exclusivePendingAction = !string.IsNullOrEmpty(state.LastPendingAction) &&
                                         (state.LastPendingAction.Equals("delete", StringComparison.OrdinalIgnoreCase) ||
                                          state.PendingInstallVersion == listingPlugin.Version);

            if (!string.IsNullOrEmpty(state.LastPendingAction))
            {
                actions.Add(CreateCancelAction(state.Identifier, state.LastPendingAction, state.PendingInstallVersion, "btn btn-outline-secondary"));
            }

            if (string.IsNullOrEmpty(state.LastPendingAction) || !exclusivePendingAction)
            {
                if (DependenciesMet(listingPlugin.Dependencies, installed))
                {
                    actions.Add(CreateInstallAction(state.Identifier, listingPlugin, "btn btn-primary", "Install"));
                }
                else
                {
                    actions.Add(CreateInstallAction(
                        state.Identifier,
                        listingPlugin,
                        "btn btn-primary",
                        "Schedule install",
                        "Schedule install for when the dependencies have been met to ensure a smooth update"));
                }
            }

            return new PluginAvailableCardViewModel
            {
                Plugin = CreateInfo(listingPlugin, installed),
                Recommended = state.Recommended,
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

            if (state.DisabledVersion is not null)
            {
                if (state.BestUpdate is not null)
                {
                    var dependenciesMet = DependenciesMet(state.BestUpdate.Dependencies, installed);
                    actions.Add(CreateInstallAction(
                        state.Identifier,
                        state.BestUpdate,
                        "btn btn-primary",
                        dependenciesMet ? "Update" : "Schedule update",
                        dependenciesMet ? null : "Schedule upgrade for when the dependencies have been met to ensure a smooth update"));
                }

                actions.Add(CreatePostAction("EnablePlugin", "Enable", "btn btn-outline-primary", state.Identifier));
                actions.Add(CreatePostAction("UnInstallPlugin", "Uninstall", "btn btn-outline-danger", state.Identifier));
                return actions;
            }

            if (state.LoadedPlugin is not null)
            {
                if (state.BestUpdate is not null)
                {
                    var dependenciesMet = DependenciesMet(state.BestUpdate.Dependencies, installed);
                    actions.Add(CreateInstallAction(
                        state.Identifier,
                        state.BestUpdate,
                        "btn btn-primary",
                        dependenciesMet ? "Update" : "Schedule update",
                        dependenciesMet ? null : "Schedule upgrade for when the dependencies have been met to ensure a smooth update"));
                }

                if (state.IsDependentOn)
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

                return actions;
            }

            if (state.BestAvailable is not null)
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
                return state.BestUpdate is not null
                    ? ("Disabled", $"Version {state.DisabledVersion} is disabled. BTCPay Server can update or re-enable it from here.")
                    : ("Disabled", $"Version {state.DisabledVersion} is disabled on this server.");
            }

            if (state.LoadedPlugin is not null)
            {
                return state.BestUpdate is not null
                    ? ("Installed", $"Version {state.BestUpdate.Version} is available for update.")
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
            Func<string, PluginState> getState,
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
                    return new SelectedState(available.Identifier, available.CatalogSlug, getState(available.Identifier));
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
                    return new SelectedState(selectedIdentifier, resolvedSlug, getState(selectedIdentifier));
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

        private static Dictionary<string, Version> NormalizeVersions(Dictionary<string, Version> versions)
        {
            return (versions ?? new Dictionary<string, Version>())
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.First().Key,
                    group => group.OrderByDescending(pair => pair.Value).First().Value,
                    StringComparer.OrdinalIgnoreCase);
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
            public bool IsDependentOn { get; init; }
            public bool Recommended { get; init; }
            public string LastPendingAction { get; init; }
            public bool HasPendingInstall { get; init; }
            public bool HasPendingDelete { get; init; }
            public bool HasPendingEnable { get; init; }
            public Version PendingInstallVersion { get; init; }
        }

        private sealed record SelectedState(string Identifier, string ResolvedSlug, PluginState State);
    }
}
