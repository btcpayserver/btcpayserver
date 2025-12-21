using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.ServerViewModels;

public class MaintenanceViewModel
{
    [Display(Name = "Domain name")]
    public string DNSDomain { get; set; }
    public bool CanUseSSH { get; internal set; }
}
