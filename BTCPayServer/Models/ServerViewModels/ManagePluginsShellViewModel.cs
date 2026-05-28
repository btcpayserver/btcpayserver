using System;
using System.Collections.Generic;

namespace BTCPayServer.Models.ServerViewModels
{
    public class ManagePluginsShellViewModel
    {
        public string SelectedPluginIdentifier { get; set; }
        public string SelectedPluginSlug { get; set; }
        public string DirectoryIframeUrl { get; set; }
        public string DirectoryOrigin { get; set; }
        public string PanelUrl { get; set; }
        public List<PluginDisabledViewModel> DisabledPlugins { get; set; } = [];
        public List<PluginInstalledCardViewModel> InstalledPlugins { get; set; } = [];
        public List<PendingPluginActionViewModel> PendingActions { get; set; } = [];
        public PluginSelectedPanelViewModel SelectedPluginPanel { get; set; } = new();
    }

    public class PluginSelectedPanelViewModel
    {
        public bool HasSelection { get; set; }
        public string SelectedIdentifier { get; set; }
        public string SelectedSlug { get; set; }
        public string EmbeddedDetailsUrl { get; set; }
        public PluginInfoViewModel Plugin { get; set; }
        public string CurrentState { get; set; }
        public string StateDescription { get; set; }
        public Version InstalledVersion { get; set; }
        public Version BestAvailableVersion { get; set; }
        public Version DisabledVersion { get; set; }
        public string PendingAction { get; set; }
        public Version PendingVersion { get; set; }
        public List<PluginActionViewModel> Actions { get; set; } = [];
    }

    public class PluginDisabledViewModel
    {
        public string Identifier { get; set; }
        public Version DisabledVersion { get; set; }
        public PluginInfoViewModel RecommendedUpdate { get; set; }
        public List<PluginActionViewModel> Actions { get; set; } = [];
    }

    public class PluginInstalledCardViewModel
    {
        public PluginInfoViewModel Current { get; set; }
        public PluginInfoViewModel Update { get; set; }
        public string PendingAction { get; set; }
        public List<PluginActionViewModel> Actions { get; set; } = [];
    }

    public class PendingPluginActionViewModel
    {
        public string Plugin { get; set; }
        public string Action { get; set; }
    }

    public class PluginInfoViewModel
    {
        public string Identifier { get; set; }
        public string CatalogSlug { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public Version Version { get; set; }
        public string Documentation { get; set; }
        public string Source { get; set; }
        public string Author { get; set; }
        public string AuthorLink { get; set; }
        public bool DependenciesMet { get; set; }
        public List<PluginDependencyViewModel> Dependencies { get; set; } = [];
    }

    public class PluginDependencyViewModel
    {
        public string Display { get; set; }
        public bool IsMet { get; set; }
    }

    public class PluginActionViewModel
    {
        public string FormAction { get; set; }
        public string Label { get; set; }
        public string CssClass { get; set; }
        public string Plugin { get; set; }
        public string Version { get; set; }
        public string Tooltip { get; set; }
        public bool Disabled { get; set; }
    }
}
