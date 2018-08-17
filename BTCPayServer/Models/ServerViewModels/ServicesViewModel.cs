using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.ServerViewModels
{
    public class ServicesViewModel
    {
        public class LNDServiceViewModel
        {
            public string Crypto { get; set; }
            public string Type { get; set; }
            public int Index { get; set; }
        }
        public List<LNDServiceViewModel> LNDServices { get; set; } = new List<LNDServiceViewModel>();
        public bool HasSSH { get; set; }
    }
}
