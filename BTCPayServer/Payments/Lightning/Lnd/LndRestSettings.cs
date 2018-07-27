using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning.Lnd
{
    public class LndRestSettings
    {
        public LndRestSettings()
        {

        }
        public LndRestSettings(Uri uri)
        {
            Uri = uri;
        }
        public Uri Uri { get; set; }
        /// <summary>
        /// The SHA256 of the PEM certificate
        /// </summary>
        public byte[] CertificateThumbprint { get; set; }
        public byte[] Macaroon { get; set; }
        public bool AllowInsecure { get; set; }
        public string MacaroonFilePath { get; set; }

        public LndAuthentication CreateLndAuthentication()
        {
            if (Macaroon != null)
                return new LndAuthentication.FixedMacaroonAuthentication(Macaroon);
            if (!string.IsNullOrEmpty(MacaroonFilePath))
                return new LndAuthentication.MacaroonFileAuthentication(MacaroonFilePath);
            return LndAuthentication.NullAuthentication.Instance;
        }
    }
}
