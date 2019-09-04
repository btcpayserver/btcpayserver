using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.SSH;
using Renci.SshNet;

namespace BTCPayServer.Models.ServerViewModels
{
    public class MaintenanceViewModel
    {
        [Display(Name = "Change domain")]
        public string DNSDomain { get; set; }
        public bool CanUseSSH { get; internal set; }
    }
}
