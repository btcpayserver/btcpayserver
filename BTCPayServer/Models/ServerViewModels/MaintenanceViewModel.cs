using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.ServerViewModels
{
    public class MaintenanceViewModel
    {
        [Display(Name = "Change domain")]
        public string DNSDomain { get; set; }
        public bool CanUseSSH { get; internal set; }
    }
}
