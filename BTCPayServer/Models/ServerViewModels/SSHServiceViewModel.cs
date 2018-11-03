using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.ServerViewModels
{
    public class SSHServiceViewModel
    {
        public string CommandLine { get; set; }
        public string Password { get; set; }
        public string KeyFilePassword { get; set; }
        public bool HasKeyFile { get; set; }
    }
}
