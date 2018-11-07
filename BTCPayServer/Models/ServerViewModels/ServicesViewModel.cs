using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration.External;

namespace BTCPayServer.Models.ServerViewModels
{
    public class ServicesViewModel
    {
        public class LNDServiceViewModel
        {
            public string Crypto { get; set; }
            public LndTypes Type { get; set; }
            public int Index { get; set; }
        }

        public List<LNDServiceViewModel> LNDServices { get; set; } = new List<LNDServiceViewModel>();
        public bool HasSSH { get; set; }
    }
}
