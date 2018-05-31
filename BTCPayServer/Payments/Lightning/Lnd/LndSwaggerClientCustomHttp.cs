using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Lightning.Lnd
{
    public class LndSwaggerClientCustomHttp : LndSwaggerClient, IDisposable
    {
        protected LndSwaggerClientCustomHttp(string baseUrl, HttpClient httpClient)
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
            var factory = new HttpClientFactoryForLnd(tlsCertificate, grpcMacaroon);
            var httpClient = factory.Generate();

            var swagger = new LndSwaggerClientCustomHttp(uri.ToString().TrimEnd('/'), httpClient);
            swagger.HttpClientFactory = factory;

            return swagger;
        }
    }

    internal class HttpClientFactoryForLnd
    {
        public HttpClientFactoryForLnd(byte[] tlsCertificate = null, byte[] grpcMacaroon = null)
        {
            TlsCertificate = tlsCertificate;
            GrpcMacaroon = grpcMacaroon;
        }

        public byte[] TlsCertificate { get; set; }
        public byte[] GrpcMacaroon { get; set; }

        public HttpClient Generate()
        {
            var httpClient = new HttpClient(GetCertificate(TlsCertificate));

            if (GrpcMacaroon != null)
            {
                var macaroonHex = BitConverter.ToString(GrpcMacaroon).Replace("-", "", StringComparison.InvariantCulture);
                httpClient.DefaultRequestHeaders.Add("Grpc-Metadata-macaroon", macaroonHex);
            }

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

    public partial class LndSwaggerClient
    {
        internal HttpClientFactoryForLnd HttpClientFactory { get; set; }

        public TaskCompletionSource<LnrpcInvoice> InvoiceResponse = new TaskCompletionSource<LnrpcInvoice>();
        public TaskCompletionSource<LndSwaggerClient> SubscribeLost = new TaskCompletionSource<LndSwaggerClient>();

        // TODO: Refactor swagger generated wrapper to include this method directly
        public async Task StartSubscribeInvoiceThread(CancellationToken token)
        {
            var urlBuilder = new StringBuilder();
            urlBuilder.Append(BaseUrl).Append("/v1/invoices/subscribe");

            using (var client = HttpClientFactory.Generate())
            {
                client.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);

                var request = new HttpRequestMessage(HttpMethod.Get, urlBuilder.ToString());

                using (var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    using (var body = await response.Content.ReadAsStreamAsync())
                    using (var reader = new StreamReader(body))
                    {
                        try
                        {
                            while (!reader.EndOfStream)
                            {
                                string line = reader.ReadLine();
                                if (line != null && line.Contains("\"result\":"))
                                {
                                    dynamic parsedJson = JObject.Parse(line);
                                    var result = parsedJson.result;
                                    var invoiceString = result.ToString();
                                    LnrpcInvoice parsedInvoice = JsonConvert.DeserializeObject<LnrpcInvoice>(invoiceString, _settings.Value);
                                    InvoiceResponse.SetResult(parsedInvoice);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            // TODO: check that the exception type is actually from a closed stream.
                            Debug.WriteLine(e.Message);
                            SubscribeLost.SetResult(this);
                        }
                    }
                }
            }
        }
    }
}
