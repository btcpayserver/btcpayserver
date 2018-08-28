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


            HttpClient _Client;
            HttpResponseMessage _Response;
            Stream _Body;
            StreamReader _Reader;
            Task _ListenLoop;

            public LndInvoiceClientSession(LndSwaggerClient parent)
            {
                _Parent = parent;
            }

            public Task StartListening()
            {
                try
                {
                    _Client = _Parent.CreateHttpClient();
                    _Client.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);
                    var request = new HttpRequestMessage(HttpMethod.Get, _Parent.BaseUrl.WithTrailingSlash() + "v1/invoices/subscribe");
                    _Parent._Authentication.AddAuthentication(request);
                    _ListenLoop = ListenLoop(request);
                }
                catch
                {
                    Dispose();
                }
                return Task.CompletedTask;
            }

            private async Task ListenLoop(HttpRequestMessage request)
            {
                try
                {
                    _Response = await _Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _Cts.Token);
                    _Body = await _Response.Content.ReadAsStreamAsync();
                    _Reader = new StreamReader(_Body);
                    while (!_Cts.IsCancellationRequested)
                    {
                        string line = await _Reader.ReadLineAsync().WithCancellation(_Cts.Token);
                        if (line != null)
                        {
                            if (line.StartsWith("{\"result\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var invoiceString = JObject.Parse(line)["result"].ToString();
                                LnrpcInvoice parsedInvoice = _Parent.Deserialize<LnrpcInvoice>(invoiceString);
                                await _Invoices.Writer.WriteAsync(ConvertLndInvoice(parsedInvoice), _Cts.Token);
                            }
                            else if (line.StartsWith("{\"error\":", StringComparison.OrdinalIgnoreCase))
                            {
                                var errorString = JObject.Parse(line)["error"].ToString();
                                var error = _Parent.Deserialize<LndError>(errorString);
                                throw new LndException(error);
                            }
                            else
                            {
                                throw new LndException("Unknown result from LND: " + line);
                            }
                        }
                    }
                }
                catch when (_Cts.IsCancellationRequested)
                {

                }
                catch (Exception ex)
                {
                    _Invoices.Writer.TryComplete(ex);
                }
                finally
                {
                    Dispose(false);
                }
            }
            public async Task<LightningInvoice> WaitInvoice(CancellationToken cancellation)
            {
                try
                {
                    return await _Invoices.Reader.ReadAsync(cancellation);
                }
                catch (ChannelClosedException ex) when (ex.InnerException == null)
                {
                    throw new TaskCanceledException();
                }
                catch (ChannelClosedException ex)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
            }

            public void Dispose()
            {
                Dispose(true);
            }
            void Dispose(bool waitLoop)
            {
                if (_Cts.IsCancellationRequested)
                    return;
                _Cts.Cancel();
                _Reader?.Dispose();
                _Reader = null;
                _Body?.Dispose();
                _Body = null;
                _Response?.Dispose();
                _Response = null;
                _Client?.Dispose();
                _Client = null;
                if (waitLoop)
                    _ListenLoop?.Wait();
                _Invoices.Writer.TryComplete();
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

            try
            {
                var node = await _rpcClient.GetNodeInfoAsync(resp.Identity_pubkey, cancellation);
                if (node.Node.Addresses == null || node.Node.Addresses.Count == 0)
                    throw new Exception("Lnd External IP not set, make sure you use --externalip=$EXTERNALIP parameter on lnd");

                var firstNodeInfo = node.Node.Addresses.First();
                var externalHostPort = firstNodeInfo.Addr.Split(':');

                nodeInfo.Address = externalHostPort[0];
                nodeInfo.P2PPort = ConvertInv.ToInt32(externalHostPort[1]);

                return nodeInfo;
            }
            catch (SwaggerException ex) when (!string.IsNullOrEmpty(ex.Response))
            {
                throw new Exception("LND threw an error: " + ex.Response);
            }
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
                if (DateTimeOffset.FromUnixTimeSeconds(invoiceExpiry) < DateTimeOffset.UtcNow)
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
