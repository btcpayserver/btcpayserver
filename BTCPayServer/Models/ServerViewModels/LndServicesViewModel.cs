using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.ServerViewModels
{
    public class LndServicesViewModel
    {
        public string Host { get; set; }
        public bool SSL { get; set; }
        public string Macaroon { get; set; }
        public string AdminMacaroon { get; set; }
        public string ReadonlyMacaroon { get; set; }
        public string InvoiceMacaroon { get; set; }
        public string CertificateThumbprint { get; set; }
        [Display(Name = "GRPC SSL Cipher suite (GRPC_SSL_CIPHER_SUITES)")]
        public string GRPCSSLCipherSuites { get; set; }
        public string QRCode { get; set; }
        public string QRCodeLink { get; set; }
        [Display(Name = "REST Uri")]
        public string Uri { get; set; }
        public string ConnectionType { get; internal set; }
    }
}
