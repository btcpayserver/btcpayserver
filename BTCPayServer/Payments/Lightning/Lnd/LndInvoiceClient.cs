using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Lightning.Lnd
{
    public class LndInvoiceClient : ILightningInvoiceClient
    {
        class LndInvoiceClientSession : ILightningListenInvoiceSession
        {
            private LndSwaggerClient _Parent;
            Channel<LightningInvoice> _Invoices = Channel.CreateBounded<LightningInvoice>(50);
            CancellationTokenSource _Cts = new CancellationTokenSource();
            ManualResetEventSlim _Stopped = new ManualResetEventSlim(false);


            HttpClient _Client;
            HttpResponseMessage _Response;
            Stream _Body;
            StreamReader _Reader;

            public LndInvoiceClientSession(LndSwaggerClient parent)
            {
                _Parent = parent;
            }

            public async Task StartListening()
            {
                _Client = _Parent.CreateHttpClient();
                _Client.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);
                var request = new HttpRequestMessage(HttpMethod.Get, _Parent.BaseUrl.WithTrailingSlash() + "v1/invoices/subscribe");
                _Response = await _Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _Cts.Token);
                _Body = await _Response.Content.ReadAsStreamAsync();
                _Reader = new StreamReader(_Body);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                ListenLoop();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            }

            private async Task ListenLoop()
            {
                try
                {
                    while (!_Cts.IsCancellationRequested)
                    {
                        string line = await _Reader.ReadLineAsync().WithCancellation(_Cts.Token);
                        if (line != null && line.StartsWith("{\"result\":", StringComparison.OrdinalIgnoreCase))
                        {
                            var invoiceString = JObject.Parse(line)["result"].ToString();
                            LnrpcInvoice parsedInvoice = _Parent.Deserialize<LnrpcInvoice>(invoiceString);
                            await _Invoices.Writer.WriteAsync(ConvertLndInvoice(parsedInvoice), _Cts.Token);
                        }
                    }
                }
                catch when (_Cts.IsCancellationRequested)
                {

                }
                catch (Exception ex)
                {
                    _Ex = ex;
                }
                finally
                {
                    _Stopped.Set();
                    Dispose();
                }
            }
            Exception _Ex;
            public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
            {
                try
                {
                    return await _Invoices.Reader.ReadAsync(cancellation);
                }
                catch when (!cancellation.IsCancellationRequested && _Ex != null)
                {
                    ExceptionDispatchInfo.Capture(_Ex).Throw();
                    throw;
                }
                catch (ChannelClosedException)
                {
                    throw new TaskCanceledException();
                }
            }

            public void Dispose()
            {
                if (_Cts.IsCancellationRequested)
                    return;

                _Reader?.Dispose();
                _Reader = null;
                _Body?.Dispose();
                _Body = null;
                _Response?.Dispose();
                _Response = null;
                _Client?.Dispose();
                _Client = null;
                
                _Cts.Cancel();
                _Stopped.Wait();
                _Invoices.Writer.Complete();
            }
        }


        public LndSwaggerClient _rpcClient;

        public LndInvoiceClient(LndSwaggerClient swaggerClient)
        {
            if (swaggerClient == null)
                throw new ArgumentNullException(nameof(swaggerClient));
            _rpcClient = swaggerClient;
        }

        public async Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
            CancellationToken cancellation = default(CancellationToken))
        {
            var strAmount = ConvertInv.ToString(amount.ToUnit(LightMoneyUnit.Satoshi));
            var strExpiry = ConvertInv.ToString(Math.Round(expiry.TotalSeconds, 0));
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


            var node = await _rpcClient.GetNodeInfoAsync(resp.Identity_pubkey, cancellation);
            if (node.Node.Addresses == null || node.Node.Addresses.Count == 0)
                throw new Exception("Lnd External IP not set, make sure you use --externalip=$EXTERNALIP parameter on lnd");

            var firstNodeInfo = node.Node.Addresses.First();
            var externalHostPort = firstNodeInfo.Addr.Split(':');

            nodeInfo.Address = externalHostPort[0];
            nodeInfo.P2PPort = ConvertInv.ToInt32(externalHostPort[1]);

            return nodeInfo;
        }

        public async Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default(CancellationToken))
        {
            var resp = await _rpcClient.LookupInvoiceAsync(invoiceId, null, cancellation);
            return ConvertLndInvoice(resp);
        }

        public async Task<ILightningListenInvoiceSession> Listen(CancellationToken cancellation = default(CancellationToken))
        {
            var session = new LndInvoiceClientSession(this._rpcClient);
            await session.StartListening();
            return session;
        }

        internal static LightningInvoice ConvertLndInvoice(LnrpcInvoice resp)
        {
            var invoice = new LightningInvoice
            {
                // TODO: Verify id corresponds to R_hash
                Id = BitString(resp.R_hash),
                Amount = new LightMoney(ConvertInv.ToInt64(resp.Value), LightMoneyUnit.Satoshi),
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


        // utility static methods... maybe move to separate class
        private static string BitString(byte[] bytes)
        {
            return BitConverter.ToString(bytes)
                .Replace("-", "", StringComparison.InvariantCulture)
                .ToLower(CultureInfo.InvariantCulture);
        }

        // Invariant culture conversion
        public static class ConvertInv
        {
            public static int ToInt32(string str)
            {
                return Convert.ToInt32(str, CultureInfo.InvariantCulture.NumberFormat);
            }

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
