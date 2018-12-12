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
            public string Type { get; set; }
            public int Index { get; set; }
            public string Action { get; internal set; }
        }

        public class ExternalService
        {
            public string Name { get; set; }
            public string Link { get; set; }
        }

        public List<LNDServiceViewModel> LNDServices { get; set; } = new List<LNDServiceViewModel>();
        public List<ExternalService> ExternalServices { get; set; } = new List<ExternalService>();
    }
}
