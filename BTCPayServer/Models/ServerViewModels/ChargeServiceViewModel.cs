using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.ServerViewModels
{
    public class ChargeServiceViewModel
    {
        public string Uri { get; set; }
        public string APIToken { get; set; }
        public string AuthenticatedUri { get; set; }
    }
}
