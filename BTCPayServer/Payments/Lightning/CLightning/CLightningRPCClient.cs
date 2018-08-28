using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Lightning.CLightning
{
    public class LightningRPCException : Exception
    {
        public LightningRPCException(string message) : base(message)
        {

        }
    }
    public class CLightningRPCClient : ILightningInvoiceClient, ILightningListenInvoiceSession
    {
        public Network Network { get; private set; }
        public Uri Address { get; private set; }

        public CLightningRPCClient(Uri address, Network network)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if(address.Scheme == "file")
            {
                address = new UriBuilder(address) { Scheme = "unix" }.Uri;
            }
            Address = address;
            Network = network;
        }

        public Task<Charge.GetInfoResponse> GetInfoAsync(CancellationToken cancellation = default(CancellationToken))
        {
            return SendCommandAsync<Charge.GetInfoResponse>("getinfo", cancellation: cancellation);
        }

        public Task SendAsync(string bolt11)
        {
            return SendCommandAsync<object>("pay", new[] { bolt11 }, true);
        }

        public async Task<PeerInfo[]> ListPeersAsync()
        {
            var peers = await SendCommandAsync<PeerInfo[]>("listpeers", isArray: true);
            foreach (var peer in peers)
            {
                peer.Channels = peer.Channels ?? Array.Empty<ChannelInfo>();
            }
            return peers;
        }

        public Task FundChannelAsync(NodeInfo nodeInfo, Money money)
        {
            return SendCommandAsync<object>("fundchannel", new object[] { nodeInfo.NodeId, money.Satoshi }, true);
        }

        public Task ConnectAsync(NodeInfo nodeInfo)
        {
            return SendCommandAsync<object>("connect", new[] { $"{nodeInfo.NodeId}@{nodeInfo.Host}:{nodeInfo.Port}" }, true);
        }

        static Encoding UTF8 = new UTF8Encoding(false);
        private async Task<T> SendCommandAsync<T>(string command, object[] parameters = null, bool noReturn = false, bool isArray = false, CancellationToken cancellation = default(CancellationToken))
        {
            parameters = parameters ?? Array.Empty<string>();
            using (Socket socket = await Connect())
            {
                using (var networkStream = new NetworkStream(socket))
                {
                    using (var textWriter = new StreamWriter(networkStream, UTF8, 1024 * 10, true))
                    {
                        using (var jsonWriter = new JsonTextWriter(textWriter))
                        {
                            var req = new JObject();
                            req.Add("id", 0);
                            req.Add("method", command);
                            req.Add("params", new JArray(parameters));
                            await req.WriteToAsync(jsonWriter, cancellation);
                            await jsonWriter.FlushAsync(cancellation);
                        }
                        await textWriter.FlushAsync();
                    }
                    await networkStream.FlushAsync(cancellation);
                    using (var textReader = new StreamReader(networkStream, UTF8, false, 1024 * 10, true))
                    {
                        using (var jsonReader = new JsonTextReader(textReader))
                        {
                            var resultAsync = JObject.LoadAsync(jsonReader, cancellation);

                            // without this hack resultAsync is blocking even if cancellation happen
                            using (cancellation.Register(() => { socket.Dispose(); }))
                            {
                                var result = await resultAsync;
                                var error = result.Property("error");
                                if (error != null)
                                {
                                    throw new LightningRPCException(error.Value["message"].Value<string>());
                                }
                                if (noReturn)
                                    return default(T);
                                if (isArray)
                                {
                                    return result["result"].Children().First().Children().First().ToObject<T>();
                                }
                                return result["result"].ToObject<T>();
                            }
                        }
                    }
                }
            }
        }

        private async Task<Socket> Connect()
        {
            Socket socket = null;
            EndPoint endpoint = null;
            if (Address.Scheme == "tcp" || Address.Scheme == "tcp")
            {
                var domain = Address.DnsSafeHost;
                if (!IPAddress.TryParse(domain, out IPAddress address))
                {
                    address = (await Dns.GetHostAddressesAsync(domain)).FirstOrDefault();
                    if (address == null)
                        throw new Exception("Host not found");
                }
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                endpoint = new IPEndPoint(address, Address.Port);
            }
            else if (Address.Scheme == "unix")
            {
                var path = Address.AbsoluteUri.Remove(0, "unix:".Length);
                if (!path.StartsWith('/'))
                    path = "/" + path;
                while (path.Length >= 2 && (path[0] != '/' || path[1] == '/'))
                {
                    path = path.Remove(0, 1);
                }
                if (path.Length < 2)
                    throw new FormatException("Invalid unix url");
                socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                endpoint = new UnixEndPoint(path);
            }
            else
                throw new NotSupportedException($"Protocol {Address.Scheme} for clightning not supported");

            await socket.ConnectAsync(endpoint);
            return socket;
        }

        public async Task<BitcoinAddress> NewAddressAsync()
        {
            var obj = await SendCommandAsync<JObject>("newaddr");
            return BitcoinAddress.Create(obj.Property("address").Value.Value<string>(), Network);
        }

        async Task<LightningInvoice> ILightningInvoiceClient.GetInvoice(string invoiceId, CancellationToken cancellation)
        {
            var invoices = await SendCommandAsync<CLightningInvoice[]>("listinvoices", new[] { invoiceId }, false, true, cancellation);
            if (invoices.Length == 0)
                return null;
            return ToLightningInvoice(invoices[0]);
        }

        static NBitcoin.DataEncoders.DataEncoder InvoiceIdEncoder = NBitcoin.DataEncoders.Encoders.Base58;
        async Task<LightningInvoice> ILightningInvoiceClient.CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation)
        {
            var id = InvoiceIdEncoder.EncodeData(RandomUtils.GetBytes(20));
            var invoice = await SendCommandAsync<CLightningInvoice>("invoice", new object[] { amount.MilliSatoshi, id, description ?? "", Math.Max(0,  (int)expiry.TotalSeconds) }, cancellation: cancellation);
            invoice.Label = id;
            invoice.MilliSatoshi = amount;
            invoice.Status = "unpaid";
            return ToLightningInvoice(invoice);
        }

        private static LightningInvoice ToLightningInvoice(CLightningInvoice invoice)
        {
            return new LightningInvoice()
            {
                Id = invoice.Label,
                Amount = invoice.MilliSatoshi,
                BOLT11 = invoice.BOLT11,
                Status = invoice.Status,
                PaidAt = invoice.PaidAt
            };
        }

        Task<ILightningListenInvoiceSession> ILightningInvoiceClient.Listen(CancellationToken cancellation)
        {
            return Task.FromResult<ILightningListenInvoiceSession>(this);
        }
        long lastInvoiceIndex = 99999999999;
        async Task<LightningInvoice> ILightningListenInvoiceSession.WaitInvoice(CancellationToken cancellation)
        {
            var invoice = await SendCommandAsync<CLightningInvoice>("waitanyinvoice", new object[] { lastInvoiceIndex }, cancellation: cancellation);
            lastInvoiceIndex = invoice.PayIndex.Value;
            return ToLightningInvoice(invoice);
        }

        async Task<LightningNodeInformation> ILightningInvoiceClient.GetInfo(CancellationToken cancellation)
        {
            var info = await GetInfoAsync(cancellation);
            return ToLightningNodeInformation(info);
        }

        internal static LightningNodeInformation ToLightningNodeInformation(Charge.GetInfoResponse info)
        {
            var addr = info.Address.FirstOrDefault();
            if (addr == null)
            {
                addr = new Charge.GetInfoResponse.GetInfoAddress();
                addr.Address = "127.0.0.1";
            }
            if (addr.Port == 0)
            {
                addr.Port = 9735;
            }
            return new LightningNodeInformation()
            {
                NodeId = info.Id,
                P2PPort = addr.Port,
                Address = addr.Address,
                BlockHeight = info.BlockHeight
            };
        }

        void IDisposable.Dispose()
        {

        }
    }
}
