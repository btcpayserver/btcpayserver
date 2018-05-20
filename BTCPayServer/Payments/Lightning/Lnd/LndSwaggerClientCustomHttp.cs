using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Payments.Lightning.Lnd
{
    public class LndSwaggerClientCustomHttp : LndSwaggerClient, IDisposable
    {
        public LndSwaggerClientCustomHttp(string baseUrl, HttpClient httpClient)
            : base(baseUrl, httpClient)
        {
            _HttpClient = httpClient;
        }

        private HttpClient _HttpClient;

        public void Dispose()
        {
            _HttpClient.Dispose();
        }

        //
        public static LndSwaggerClientCustomHttp Create(Uri uri, Network network, byte[] tlsCertificate = null, byte[] grpcMacaroon = null)
        {
            // for development we are working with custom build of lnd that allows no macaroons and http
            var clientWithNoMacaroonsTls = tlsCertificate == null || grpcMacaroon == null;

            var httpClient = clientWithNoMacaroonsTls ? new HttpClient() :
                HttpClientFactoryForLnd.Generate(tlsCertificate, grpcMacaroon);

            return new LndSwaggerClientCustomHttp(uri.ToString().TrimEnd('/'), httpClient);
        }
    }

    internal class HttpClientFactoryForLnd
    {
        internal static HttpClient Generate(byte[] tlsCertificate, byte[] grpcMacaroon)
        {
            var httpClient = new HttpClient(GetCertificate(tlsCertificate));
            var macaroonHex = BitConverter.ToString(grpcMacaroon).Replace("-", "", StringComparison.InvariantCulture);
            httpClient.DefaultRequestHeaders.Add("Grpc-Metadata-macaroon", macaroonHex);

            return httpClient;
        }

        private static HttpClientHandler GetCertificate(byte[] certFile)
        {
            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12
            };
            if (certFile == null)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                return handler;
            }

            // if certificate is not null, try with custom accepting logic
            X509Certificate2 clientCertificate = null;
            if (certFile != null)
                clientCertificate = new X509Certificate2(certFile);

            handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
            {
                const SslPolicyErrors unforgivableErrors =
                    SslPolicyErrors.RemoteCertificateNotAvailable |
                    SslPolicyErrors.RemoteCertificateNameMismatch;

                if ((errors & unforgivableErrors) != 0)
                {
                    return false;
                }

                if (clientCertificate == null)
                    return true;

                X509Certificate2 remoteRoot = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                var res = clientCertificate.RawData.SequenceEqual(remoteRoot.RawData);

                return res;
            };

            return handler;
        }
    }
}
