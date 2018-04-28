using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Payments.Lightning.Lnd
{
    public class LndClient : ILightningInvoiceClient, ILightningListenInvoiceSession
    {
        public LndClient(Uri uri, Network network, byte[] tlsCertificate, byte[] grpcMacaroon)
        {
            _HttpClient = HttpClientFactoryForLnd.Generate(tlsCertificate, grpcMacaroon);
            _Decorator = new LndSwaggerClient(uri.ToString().TrimEnd('/'), _HttpClient);
        }

        private HttpClient _HttpClient;
        private LndSwaggerClient _Decorator;

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation = default(CancellationToken))
        {
            var resp = await _Decorator.AddInvoiceAsync(new LnrpcInvoice
            {
                Value = (long)amount.ToDecimal(LightMoneyUnit.Satoshi),
                Memo = description,
                Expiry = (long)expiry.TotalSeconds
            });

            var invoice = new LightningInvoice
            {
                // TODO: Verify id corresponds to R_hash
                Id = BitString(resp.R_hash),
                Amount = amount,
                BOLT11 = resp.Payment_request,
                Status = "unpaid"
            };
            return invoice;
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default(CancellationToken))
        {
            var resp = await _Decorator.GetInfoAsync(cancellation);

            var invoice = new LightningNodeInformation
            {
                Address = resp.Uris?.FirstOrDefault(),
                BlockHeight = (int?)resp.Block_height ?? 0,
                NodeId = resp.Alias,
                P2PPort = 0
            };
            return invoice;
        }

        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default(CancellationToken))
        {
            var resp = await _Decorator.LookupInvoiceAsync(invoiceId, null, cancellation);
            return ConvertLndInvoice(resp);
        }

        public Task<ILightningListenInvoiceSession> Listen(CancellationToken cancellation = default(CancellationToken))
        {
            return Task.FromResult<ILightningListenInvoiceSession>(this);
        }

        async Task<LightningInvoice> ILightningListenInvoiceSession.WaitInvoice(CancellationToken cancellation)
        {
            var resp = await _Decorator.SubscribeInvoicesAsync(cancellation);
            return ConvertLndInvoice(resp);

        }

        public void Dispose()
        {
            _HttpClient?.Dispose();
        }

        private static string BitString(byte[] bytes)
        {
            return BitConverter.ToString(bytes)
                .Replace("-", "", StringComparison.InvariantCulture)
                .ToLower(CultureInfo.InvariantCulture);
        }

        private static LightningInvoice ConvertLndInvoice(LnrpcInvoice resp)
        {
            var invoice = new LightningInvoice
            {
                // TODO: Verify id corresponds to R_hash
                Id = BitString(resp.R_hash),
                Amount = resp.Value,
                BOLT11 = resp.Payment_request,
                Status = "unpaid"
            };

            if (resp.Settle_date.HasValue)
            {
                invoice.PaidAt = DateTimeOffset.FromUnixTimeSeconds(resp.Settle_date.Value);
                invoice.Status = "paid";
            }
            else if (DateTimeOffset.FromUnixTimeSeconds(resp.Creation_date.Value + resp.Expiry.Value) > DateTimeOffset.UtcNow)
            {
                invoice.Status = "expired";
            }
            return invoice;
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
            var clientCertificate = new X509Certificate2(certFile);

            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12
            };

            handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
            {
                const SslPolicyErrors unforgivableErrors =
                    SslPolicyErrors.RemoteCertificateNotAvailable |
                    SslPolicyErrors.RemoteCertificateNameMismatch;

                if ((errors & unforgivableErrors) != 0)
                {
                    return false;
                }

                X509Certificate2 remoteRoot = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
                var res = clientCertificate.RawData.SequenceEqual(remoteRoot.RawData);

                return res;
            };

            return handler;
        }
    }

    partial class LndSwaggerClient
    {
    }
}
