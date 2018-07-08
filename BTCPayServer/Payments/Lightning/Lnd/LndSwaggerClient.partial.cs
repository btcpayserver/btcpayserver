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
        public X509Certificate2 TLS { get; set; }
        public byte[] Macaroon { get; set; }
    }

    public partial class LndSwaggerClient
    {
        public LndSwaggerClient(LndRestSettings settings)
            : this(settings.Uri.AbsoluteUri.TrimEnd('/'), CreateHttpClient(settings))
        {
            _Settings = settings;
        }
        LndRestSettings _Settings;
        private static HttpClient CreateHttpClient(LndRestSettings settings)
        {
            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12
            };

            var expectedCertificate = settings.TLS;
            if (expectedCertificate != null)
            {
                handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
                {
                    X509Certificate2 remoteRoot = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                    return expectedCertificate.RawData.SequenceEqual(remoteRoot.RawData);
                };
            }
            var httpClient = new HttpClient(handler);
            if (settings.Macaroon != null)
            {
                var macaroonHex = BitConverter.ToString(settings.Macaroon).Replace("-", "", StringComparison.InvariantCulture);
                httpClient.DefaultRequestHeaders.Add("Grpc-Metadata-macaroon", macaroonHex);
            }
            return httpClient;
        }

        public TaskCompletionSource<LnrpcInvoice> InvoiceResponse = new TaskCompletionSource<LnrpcInvoice>();
        public TaskCompletionSource<LndSwaggerClient> SubscribeLost = new TaskCompletionSource<LndSwaggerClient>();

        // TODO: Refactor swagger generated wrapper to include this method directly
        public async Task StartSubscribeInvoiceThread(CancellationToken token)
        {
            var urlBuilder = new StringBuilder();
            urlBuilder.Append(BaseUrl).Append("/v1/invoices/subscribe");

            using (var client = CreateHttpClient(_Settings))
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
                                if (line != null && line.Contains("\"result\":", StringComparison.OrdinalIgnoreCase))
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
