using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.RPC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Lightning.CLightning.RPC
{
    public class CLightningRPCClient
    {
        public Network Network { get; private set; }
        public Uri Address { get; private set; }

        public CLightningRPCClient(Uri address, Network network)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            Address = address;
            Network = network;
        }

        public Task<GetInfoResponse> GetInfoAsync()
        {
            return SendCommandAsync<GetInfoResponse>("getinfo");
        }

        public Task SendAsync(string bolt11)
        {
            return SendCommandAsync<object>("pay", new[] { bolt11 }, true);
        }

        public async Task<PeerInfo[]> ListPeersAsync()
        {
            var peers = await SendCommandAsync<PeerInfo[]>("listpeers", isArray: true);
            foreach(var peer in peers)
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
        private async Task<T> SendCommandAsync<T>(string command, object[] parameters = null, bool noReturn = false, bool isArray = false)
        {
            parameters = parameters ?? Array.Empty<string>();
            var domain = Address.DnsSafeHost;
            if (!IPAddress.TryParse(domain, out IPAddress address))
            {
                address = (await Dns.GetHostAddressesAsync(domain)).FirstOrDefault();
                if (address == null)
                    throw new Exception("Host not found");
            }
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(new IPEndPoint(address, Address.Port));
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
                        await req.WriteToAsync(jsonWriter);
                        await jsonWriter.FlushAsync();
                    }
                    await textWriter.FlushAsync();
                }
                await networkStream.FlushAsync();
                using (var textReader = new StreamReader(networkStream, UTF8, false, 1024 * 10, true))
                {
                    using (var jsonReader = new JsonTextReader(textReader))
                    {
                        var result = await JObject.LoadAsync(jsonReader);
                        var error = result.Property("error");
                        if(error != null)
                        {
                            throw new Exception(error.Value.ToString());
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

        public async Task<BitcoinAddress> NewAddressAsync()
        {
            var obj = await SendCommandAsync<JObject>("newaddr");
            return BitcoinAddress.Create(obj.Property("address").Value.Value<string>(), Network);
        }
    }
}
