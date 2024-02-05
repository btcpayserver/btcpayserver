using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.ServerViewModels;

public class MaintenanceViewModel
{
    [Display(Name = "Domain name")]
    public string DNSDomain { get; set; }
    public bool CanUseSSH { get; internal set; }

    [Display(Name = "Discourage search engines from indexing this site")]
    public bool DiscourageSearchEngines { get; set; }

    [Display(Name = "Check releases on GitHub and notify when new BTCPay Server version is available")]
    public bool CheckForNewVersions { get; set; }

    [Display(Name = "Enable experimental features")]
    public bool EnableExperimentalFeatures { get; set; }
}
