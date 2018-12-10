using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.ServerViewModels
{
    public class LndGrpcServicesViewModel
    {
        public string Host { get; set; }
        public bool SSL { get; set; }
        public string Macaroon { get; set; }
        public string RestrictedMacaroon { get; set; }
        public string CertificateThumbprint { get; set; }
        public string QRCode { get; set; }
        public string QRCodeLink { get; set; }
        [Display(Name = "REST Uri")]
        public string Uri { get; set; }
        public string ConnectionType { get; internal set; }
    }
}
