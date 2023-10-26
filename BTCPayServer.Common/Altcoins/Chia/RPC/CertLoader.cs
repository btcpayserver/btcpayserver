using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace BTCPayServer.Common.Altcoins.Chia.RPC
{
    /// <summary>
    /// Helper class for loading certificates
    /// </summary>
    internal static class CertLoader
    {
        /// <summary>
        /// Constructs an ephemeral <see cref="X509Certificate2"/> from a crt and key stored as files
        /// </summary>
        /// <param name="certPath">The full path the public cert (.crt)</param>
        /// <param name="keyPath">The full path to the RSA encoded private key (.key)</param>
        /// <returns>An ephemeral certificate that can be used for WebSocket authentication</returns>
        public static X509Certificate2Collection GetCerts(string certPath, string keyPath)
        {
            if (!File.Exists(certPath))
            {
                throw new FileNotFoundException($"crt file {certPath} not found");
            }

            if (!File.Exists(keyPath))
            {
                throw new FileNotFoundException($"key file {keyPath} not found");
            }

            using X509Certificate2 cert = new(certPath);
            using StreamReader streamReader = new(keyPath);

            var base64 = new StringBuilder(streamReader.ReadToEnd())
                .Replace("-----BEGIN RSA PRIVATE KEY-----", string.Empty)
                .Replace("-----BEGIN PRIVATE KEY-----", string.Empty)
                .Replace("-----END RSA PRIVATE KEY-----", string.Empty)
                .Replace("-----END PRIVATE KEY-----", string.Empty)
                .Replace(Environment.NewLine, string.Empty)
                .ToString();
            
            using var rsa = RSA.Create();
            try
            {
                rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(base64), out _);
            }
            catch
            {
                rsa.ImportRSAPrivateKey(Convert.FromBase64String(base64), out _);
            }

            using var certWithKey = cert.CopyWithPrivateKey(rsa);
            var ephemeralCert = new X509Certificate2(certWithKey.Export(X509ContentType.Pkcs12));

            return new(ephemeralCert);
        }
    }
}
