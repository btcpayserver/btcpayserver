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
    public class LndInvoiceClient : ILightningInvoiceClient, ILightningListenInvoiceSession
    {
        public LndSwaggerClient _rpcClient;

        public LndInvoiceClient(LndSwaggerClient rpcClient)
        {
            _rpcClient = rpcClient;
        }

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation = default(CancellationToken))
        {
            var strAmount = ConvertInv.ToString(amount.ToUnit(LightMoneyUnit.Satoshi));
            var strExpiry = ConvertInv.ToString(expiry.TotalSeconds);
            // lnd requires numbers sent as strings. don't ask
            var resp = await _rpcClient.AddInvoiceAsync(new LnrpcInvoice
            {
                Value = strAmount,
                Memo = description,
                Expiry = strExpiry
            });

            var invoice = new LightningInvoice
            {
                Id = BitString(resp.R_hash),
                Amount = amount,
                BOLT11 = resp.Payment_request,
                Status = "unpaid"
            };
            return invoice;
        }

        public async Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default(CancellationToken))
        {
            var resp = await _rpcClient.GetInfoAsync(cancellation);

            var nodeInfo = new LightningNodeInformation
            {
                BlockHeight = (int?)resp.Block_height ?? 0,
                NodeId = resp.Identity_pubkey
            };

            // Lnd doesn't return this data as Clightning, find alternative ways to supply
            // it always should be merchant_lnd:9735 because of docker
            nodeInfo.Address = null;
            nodeInfo.P2PPort = 9735;

            return nodeInfo;
        }

        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default(CancellationToken))
        {
            var resp = await _rpcClient.LookupInvoiceAsync(invoiceId, null, cancellation);
            return ConvertLndInvoice(resp);
        }
        
        public Task<ILightningListenInvoiceSession> Listen(CancellationToken cancellation = default(CancellationToken))
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _rpcClient.StartSubscribeInvoiceThread(cancellation);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            return Task.FromResult<ILightningListenInvoiceSession>(this);
        }

        async Task<LightningInvoice> ILightningListenInvoiceSession.WaitInvoice(CancellationToken cancellation)
        {
            var resp = await _rpcClient.InvoiceResponse.Task;
            return ConvertLndInvoice(resp);
        }


        // utility static methods... maybe move to separate class
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

            if (resp.Settle_date != null)
            {
                invoice.PaidAt = DateTimeOffset.FromUnixTimeSeconds(ConvertInv.ToInt64(resp.Settle_date));
                invoice.Status = "paid";
            }
            else
            {
                var invoiceExpiry = ConvertInv.ToInt64(resp.Creation_date) + ConvertInv.ToInt64(resp.Expiry);
                if (DateTimeOffset.FromUnixTimeSeconds(invoiceExpiry) > DateTimeOffset.UtcNow)
                {
                    invoice.Status = "expired";
                }
            }
            return invoice;
        }

        public void Dispose()
        {
            //
        }

        // Invariant culture conversion
        public static class ConvertInv
        {
            public static long ToInt64(string str)
            {
                return Convert.ToInt64(str, CultureInfo.InvariantCulture.NumberFormat);
            }

            public static string ToString(decimal d)
            {
                return d.ToString(CultureInfo.InvariantCulture);
            }

            public static string ToString(double d)
            {
                return d.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
