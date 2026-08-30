using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Maintenance.Models;

public class MaintenanceViewModel
{
    [Display(Name = "Domain name")]
    public string DNSDomain { get; set; }
    public HashSet<string> SupportedCommands { get; set; }
}
