using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.ServerViewModels
{
    public class LndRestServicesViewModel
    {
        public string BaseApiUrl { get; set; }
        public string Macaroon { get; set; }
        public string CertificateThumbprint { get; set; }
    }
}
