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
            public string SelectedPluginSlug { get; set; }
        }

        public InstalledPluginsViewModel CreateInstalledPluginsViewModel(ProjectionSource source)
        {
            source ??= new ProjectionSource();
            var data = GetManagePluginsProjectionData(source);

            return new InstalledPluginsViewModel
            {
                DisabledPlugins = data.DisabledVersions
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => CreateDisabledViewModel(
                        ComputePluginState(pair.Key, data),
                        data.InstalledVersions))
                    .ToList(),
                InstalledPlugins = data.LoadedPlugins
                    .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(plugin => CreateInstalledPluginCardViewModel(
                        ComputePluginState(plugin.Identifier, data),
                        data.InstalledVersions,
                        IsRequiredByOtherPlugins(plugin.Identifier, data)))
                    .ToList(),
                PendingActions = CreatePendingActions(data.PendingCommands)
            };
        }

        public PluginDirectoryViewModel CreatePluginDirectoryViewModel(ProjectionSource source)
        {
            source ??= new ProjectionSource();
            var data = GetManagePluginsProjectionData(source);
            var selectedPanel = CreateSelectedPanel(source.SelectedPluginSlug, data);
            var hiddenPluginIdentifiers = data.LoadedPlugins
                .Select(plugin => plugin.Identifier)
                .Concat(data.DisabledVersions.Keys)
                .Where(identifier => !string.IsNullOrEmpty(identifier))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(identifier => identifier, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new PluginDirectoryViewModel
            {
                SelectedPluginSlug = selectedPanel.SelectedSlug,
                HiddenPluginIdentifiers = hiddenPluginIdentifiers,
                PendingActions = CreatePendingActions(data.PendingCommands),
                SelectedPluginPanel = selectedPanel
            };
        }

        public PluginSelectedPanelViewModel CreateSelectedPluginPanelViewModel(ProjectionSource source)
        {
            source ??= new ProjectionSource();
            return CreateSelectedPanel(source.SelectedPluginSlug, GetManagePluginsProjectionData(source));
        }

        private static ManagePluginsProjectionData GetManagePluginsProjectionData(ProjectionSource source)
        {
            var loadedPlugins = (source.LoadedPlugins ?? [])
                .Where(plugin => !plugin.SystemPlugin)
                .ToArray();
            var installedVersions = source.Installed ?? new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            var pendingCommands = source.Commands ?? [];
            var disabledVersions = source.Disabled ?? new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
            var availablePlugins = (source.AllAvailable ?? [])
                .Where(plugin => plugin is not null)
                .ToArray();
            var availableVersionsByIdentifier = availablePlugins
                .GroupBy(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(plugin => plugin.Version).ToArray(), StringComparer.OrdinalIgnoreCase);
            var pendingInstallVersions = pendingCommands
                .Where(tuple => tuple.command.Equals("install", StringComparison.OrdinalIgnoreCase))
                .Select(tuple => tuple.plugin)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    plugin => plugin,
                    plugin => source.GetVersionOfPendingInstall?.Invoke(plugin),
                    StringComparer.OrdinalIgnoreCase);

            return new ManagePluginsProjectionData
            {
                LoadedPlugins = loadedPlugins,
                InstalledVersions = installedVersions,
                PendingCommands = pendingCommands,
                DisabledVersions = disabledVersions,
                AvailablePlugins = availablePlugins,
                AvailableVersionsByIdentifier = availableVersionsByIdentifier,
                PendingInstallVersions = pendingInstallVersions
            };
        }

        private PluginSelectedPanelViewModel CreateSelectedPanel(string selectedSlug, ManagePluginsProjectionData data)
        {
            if (string.IsNullOrEmpty(selectedSlug))
                return new PluginSelectedPanelViewModel { HasSelection = false };

            var selectedAvailable = data.AvailablePlugins.FirstOrDefault(plugin =>
                plugin.CatalogSlug != null &&
                plugin.CatalogSlug.Equals(selectedSlug, StringComparison.OrdinalIgnoreCase));
            if (selectedAvailable is null)
                return new PluginSelectedPanelViewModel { HasSelection = false };

            var state = ComputePluginState(selectedAvailable.Identifier, data);
            PluginInfoViewModel plugin;
            if (state.DisabledVersion is not null)
            {
                plugin = CreatePluginInfo(state.BestUpdate ?? state.BestAvailable, data.InstalledVersions) ?? new PluginInfoViewModel
                {
                    Identifier = state.Identifier,
                    Name = state.Identifier,
                    Version = state.DisabledVersion
                };
            }
            else if (state.LoadedPlugin is not null)
            {
                plugin = CreatePluginInfo(state.LoadedPlugin, state.BestAvailable, data.InstalledVersions);
            }
            else
            {
                plugin = CreatePluginInfo(state.BestAvailable, data.InstalledVersions);
            }
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
                var dependenciesMet = PluginManager.DependenciesMet(state.BestAvailable.Dependencies ?? [], data.InstalledVersions);
                actions.Add(CreateInstallAction(
                    state.Identifier,
                    state.BestAvailable,
                    "btn btn-primary",
                    dependenciesMet ? "Install" : "Schedule install",
                    dependenciesMet ? null : "Schedule install for when the dependencies have been met to ensure a smooth update"));
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
                Actions = actions
            };
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

        private static PluginState ComputePluginState(string identifier, ManagePluginsProjectionData data)
        {
            data.AvailableVersionsByIdentifier.TryGetValue(identifier, out var availableVersions);
            var bestAvailable = GetBestCandidate(availableVersions, data.InstalledVersions);

            var loadedPlugin = data.LoadedPlugins.FirstOrDefault(plugin =>
                plugin.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            data.DisabledVersions.TryGetValue(identifier, out var disabledVersion);

            var installedVersion = loadedPlugin?.Version;
            var currentVersion = installedVersion ?? disabledVersion;
            var updateCandidates = currentVersion is null
                ? []
                : availableVersions?
                    .Where(plugin => plugin.Version > currentVersion)
                    .ToArray() ?? [];

            var bestUpdate = GetBestCandidate(updateCandidates, data.InstalledVersions);

            data.PendingInstallVersions.TryGetValue(identifier, out var pendingVersion);
            var pluginCommands = data.PendingCommands
                .Where(tuple => tuple.plugin.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                .ToArray();

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
                PendingInstallVersion = pendingVersion
            };
        }

        private static bool IsRequiredByOtherPlugins(string plugin, ManagePluginsProjectionData data)
        {
            var pendingDeletePlugins = data.PendingCommands
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

            foreach (var pendingPlugin in data.PendingCommands
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

        private PluginInstalledCardViewModel CreateInstalledPluginCardViewModel(
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

        private static PluginService.AvailablePlugin GetBestCandidate(IEnumerable<PluginService.AvailablePlugin> plugins, Dictionary<string, Version> installed)
        {
            var ordered = plugins?
                .OrderByDescending(plugin => plugin.Version)
                .ToArray() ?? [];
            return ordered.FirstOrDefault(plugin => PluginManager.DependenciesMet(plugin.Dependencies ?? [], installed)) ?? ordered.FirstOrDefault();
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

        private static PluginInfoViewModel CreatePluginInfo(PluginService.AvailablePlugin plugin, Dictionary<string, Version> installed)
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
            PluginService.AvailablePlugin metadata,
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

        private sealed class PluginState
        {
            public string Identifier { get; init; }
            public IBTCPayServerPlugin LoadedPlugin { get; init; }
            public Version DisabledVersion { get; init; }
            public PluginService.AvailablePlugin BestAvailable { get; init; }
            public PluginService.AvailablePlugin BestUpdate { get; init; }
            public string LastPendingAction { get; init; }
            public bool HasPendingInstall { get; init; }
            public bool HasPendingDelete { get; init; }
            public bool HasPendingEnable { get; init; }
            public Version PendingInstallVersion { get; init; }
        }

        private sealed class ManagePluginsProjectionData
        {
            public IBTCPayServerPlugin[] LoadedPlugins { get; init; }
            public Dictionary<string, Version> InstalledVersions { get; init; }
            public (string command, string plugin)[] PendingCommands { get; init; }
            public Dictionary<string, Version> DisabledVersions { get; init; }
            public PluginService.AvailablePlugin[] AvailablePlugins { get; init; }
            public Dictionary<string, PluginService.AvailablePlugin[]> AvailableVersionsByIdentifier { get; init; }
            public Dictionary<string, Version> PendingInstallVersions { get; init; }
        }

    }
}
