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
        public bool ExposedSSH { get; set; }
        [Required]
        public string UserName { get; set; }
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
        [Display(Name = "Change domain")]
        public string DNSDomain { get; set; }
        public SshClient CreateSSHClient(string host)
        {
            return new SshClient(host, UserName, Password);
        }

        internal void SetConfiguredSSH(SSHSettings settings)
        {
            if(settings != null)
            {
                ExposedSSH = true;
                UserName = "unknown";
                Password = "unknown";
            }
        }
    }
}
