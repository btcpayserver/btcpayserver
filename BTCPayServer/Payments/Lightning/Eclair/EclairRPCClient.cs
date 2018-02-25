using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.JsonConverters;
using NBitcoin.RPC;

namespace BTCPayServer.Payments.Lightning.Eclair
{
    public class SendResponse
    {
        public string PaymentHash { get; set; }
    }
    public class ChannelInfo
    {
        public string NodeId { get; set; }
        public string ChannelId { get; set; }
        public string State { get; set; }
    }
    public class EclairRPCClient
    {
        public EclairRPCClient(Uri address, Network network)
        {
            if (address == null)
                throw new ArgumentNullException(nameof(address));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            Address = address;
            Network = network;
            if (string.IsNullOrEmpty(address.UserInfo))
                throw new ArgumentException(paramName: nameof(address), message: "User info in the url should be provided");
            Password = address.UserInfo;
        }

        public string Password { get; set; }

        public Network Network { get; private set; }


        public GetInfoResponse GetInfo()
        {
            return GetInfoAsync().GetAwaiter().GetResult();
        }

        public Task<GetInfoResponse> GetInfoAsync()
        {
            return SendCommandAsync<GetInfoResponse>(new RPCRequest("getinfo", Array.Empty<object>()));
        }

        public async Task<T> SendCommandAsync<T>(RPCRequest request, bool throwIfRPCError = true)
        {
            var response = await SendCommandAsync(request, throwIfRPCError);
            return Serializer.ToObject<T>(response.ResultString, Network);
        }

        public async Task<RPCResponse> SendCommandAsync(RPCRequest request, bool throwIfRPCError = true)
        {
            RPCResponse response = null;
            HttpWebRequest webRequest = response == null ? CreateWebRequest() : null;
            if (response == null)
            {
                var writer = new StringWriter();
                request.WriteJSON(writer);
                writer.Flush();
                var json = writer.ToString();
                var bytes = Encoding.UTF8.GetBytes(json);
#if !(PORTABLE || NETCORE)
                webRequest.ContentLength = bytes.Length;
#endif
                var dataStream = await webRequest.GetRequestStreamAsync().ConfigureAwait(false);
                await dataStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await dataStream.FlushAsync().ConfigureAwait(false);
                dataStream.Dispose();
            }
            WebResponse webResponse = null;
            WebResponse errorResponse = null;
            try
            {
                webResponse = response == null ? await webRequest.GetResponseAsync().ConfigureAwait(false) : null;
                response = response ?? RPCResponse.Load(await ToMemoryStreamAsync(webResponse.GetResponseStream()).ConfigureAwait(false));

                if (throwIfRPCError)
                    response.ThrowIfError();
            }
            catch (WebException ex)
            {
                if (ex.Response == null || ex.Response.ContentLength == 0 ||
                    !ex.Response.ContentType.Equals("application/json", StringComparison.Ordinal))
                    throw;
                errorResponse = ex.Response;
                response = RPCResponse.Load(await ToMemoryStreamAsync(errorResponse.GetResponseStream()).ConfigureAwait(false));
                if (throwIfRPCError)
                    response.ThrowIfError();
            }
            finally
            {
                if (errorResponse != null)
                {
                    errorResponse.Dispose();
                    errorResponse = null;
                }
                if (webResponse != null)
                {
                    webResponse.Dispose();
                    webResponse = null;
                }
            }
            return response;
        }

        public AllChannelResponse[] AllChannels()
        {
            return AllChannelsAsync().GetAwaiter().GetResult();
        }

        public async Task<AllChannelResponse[]> AllChannelsAsync()
        {
            return await SendCommandAsync<AllChannelResponse[]>(new RPCRequest("allchannels", Array.Empty<object>())).ConfigureAwait(false);
        }

        public ChannelInfo[] Channels()
        {
            return ChannelsAsync().GetAwaiter().GetResult();
        }

        public async Task<ChannelInfo[]> ChannelsAsync()
        {
            return await SendCommandAsync<ChannelInfo[]>(new RPCRequest("channels", Array.Empty<object>())).ConfigureAwait(false);
        }

        public void Close(string channelId)
        {
            CloseAsync(channelId).GetAwaiter().GetResult();
        }

        public async Task SendAsync(string paymentRequest)
        {
            await SendCommandAsync<SendResponse>(new RPCRequest("send", new[] { paymentRequest })).ConfigureAwait(false);
        }

        public async Task CloseAsync(string channelId)
        {
            if (channelId == null)
                throw new ArgumentNullException(nameof(channelId));
            try
            {
                await SendCommandAsync(new RPCRequest("close", new object[] { channelId })).ConfigureAwait(false);
            }
            catch (RPCException ex) when (ex.Message == "closing already in progress")
            {
                
            }
        }

        public ChannelResponse Channel(string channelId)
        {
            return ChannelAsync(channelId).GetAwaiter().GetResult();
        }

        public async Task<ChannelResponse> ChannelAsync(string channelId)
        {
            if (channelId == null)
                throw new ArgumentNullException(nameof(channelId));
            return await SendCommandAsync<ChannelResponse>(new RPCRequest("channel", new object[] { channelId })).ConfigureAwait(false);
        }

        public string[] AllNodes()
        {
            return AllNodesAsync().GetAwaiter().GetResult();
        }

        public async Task<string[]> AllNodesAsync()
        {
            return await SendCommandAsync<string[]>(new RPCRequest("allnodes", Array.Empty<object>())).ConfigureAwait(false);
        }

        public Uri Address { get; private set; }

        private HttpWebRequest CreateWebRequest()
        {
            var webRequest = (HttpWebRequest)WebRequest.Create(Address.AbsoluteUri);
            webRequest.ContentType = "application/json";
            webRequest.Method = "POST";
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes(Password));
            webRequest.Headers[HttpRequestHeader.Authorization] = $"Basic {auth}";
            return webRequest;
        }


        private async Task<Stream> ToMemoryStreamAsync(Stream stream)
        {
            MemoryStream ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(false);
            ms.Position = 0;
            return ms;
        }

        public string Open(NodeInfo node, Money fundingSatoshi, LightMoney pushAmount = null)
        {
            return OpenAsync(node, fundingSatoshi, pushAmount).GetAwaiter().GetResult();
        }

        public string Connect(NodeInfo node)
        {
            return ConnectAsync(node).GetAwaiter().GetResult();
        }

        public async Task<string> ConnectAsync(NodeInfo node)
        {
            if (node == null)
                throw new ArgumentNullException(nameof(node));
            return (await SendCommandAsync(new RPCRequest("connect", new object[] { node.NodeId, node.Host, node.Port })).ConfigureAwait(false)).ResultString;
        }

        public string Receive(LightMoney amount, string description = null)
        {
            return ReceiveAsync(amount, description).GetAwaiter().GetResult();
        }

        public async Task<string> ReceiveAsync(LightMoney amount, string description = null)
        {
            if (amount == null)
                throw new ArgumentNullException(nameof(amount));
            List<object> args = new List<object>();
            args.Add(amount.MilliSatoshi);
            if(description != null)
            {
                args.Add(description);
            }
            return (await SendCommandAsync(new RPCRequest("receive", args.ToArray())).ConfigureAwait(false)).ResultString;
        }

        public async Task<string> OpenAsync(NodeInfo node, Money fundingSatoshi, LightMoney pushAmount = null)
        {
            if (fundingSatoshi == null)
                throw new ArgumentNullException(nameof(fundingSatoshi));
            if (node == null)
                throw new ArgumentNullException(nameof(node));
            pushAmount = pushAmount ?? LightMoney.Zero;

            var result = await SendCommandAsync(new RPCRequest("open", new object[] { node.NodeId, fundingSatoshi.Satoshi, pushAmount.MilliSatoshi }));

            return result.ResultString;
        }


    }
}
