using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBXplorer;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning.Charge
{
    public class ChargeClient : ILightningInvoiceClient
    {
        private Uri _Uri;
        public Uri Uri
        {
            get
            {
                return _Uri;
            }
        }
        private Network _Network;
        static HttpClient _Client = new HttpClient();

        public ChargeClient(Uri uri, Network network)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            this._Uri = uri;
            this._Network = network;
            if (uri.UserInfo == null)
                throw new ArgumentException(paramName: nameof(uri), message: "User information not present in uri");
            var userInfo = uri.UserInfo.Split(':');
            if (userInfo.Length != 2)
                throw new ArgumentException(paramName: nameof(uri), message: "User information not present in uri");
            Credentials = new NetworkCredential(userInfo[0], userInfo[1]);
        }

        public async Task<CreateInvoiceResponse> CreateInvoiceAsync(CreateInvoiceRequest request, CancellationToken cancellation = default(CancellationToken))
        {
            var message = CreateMessage(HttpMethod.Post, "invoice");
            Dictionary<string, string> parameters = new Dictionary<string, string>();
            parameters.Add("msatoshi", request.Amount.MilliSatoshi.ToString(CultureInfo.InvariantCulture));
            parameters.Add("expiry", ((int)request.Expiry.TotalSeconds).ToString(CultureInfo.InvariantCulture));
            if(request.Description != null)
                parameters.Add("description", request.Description);
            message.Content = new FormUrlEncodedContent(parameters);
            var result = await _Client.SendAsync(message, cancellation);
            result.EnsureSuccessStatusCode();
            var content = await result.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<CreateInvoiceResponse>(content);
        }

        public async Task<ChargeSession> Listen(CancellationToken cancellation = default(CancellationToken))
        {
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", $"Basic {GetBase64Creds()}");
            var uri = new UriBuilder(Uri) { UserName = null, Password = null }.Uri.AbsoluteUri;
            if (!uri.EndsWith('/'))
                uri += "/";
            uri += "ws";
            uri = ToWebsocketUri(uri);
            await socket.ConnectAsync(new Uri(uri), cancellation);
            return new ChargeSession(socket);
        }

        private static string ToWebsocketUri(string uri)
        {
            if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                uri = uri.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase);
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                uri = uri.Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase);
            return uri;
        }

        public NetworkCredential Credentials { get; set; }

        public GetInfoResponse GetInfo()
        {
            return GetInfoAsync().GetAwaiter().GetResult();
        }

        public async Task<ChargeInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default(CancellationToken))
        {
            var request = CreateMessage(HttpMethod.Get, $"invoice/{invoiceId}");
            var message = await _Client.SendAsync(request, cancellation);
            if (message.StatusCode == HttpStatusCode.NotFound)
                return null;
            message.EnsureSuccessStatusCode();
            var content = await message.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ChargeInvoice>(content);
        }

        public async Task<GetInfoResponse> GetInfoAsync(CancellationToken cancellation = default(CancellationToken))
        {
            var request = CreateMessage(HttpMethod.Get, "info");
            var message = await _Client.SendAsync(request, cancellation);
            message.EnsureSuccessStatusCode();
            var content = await message.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GetInfoResponse>(content);
        }

        private HttpRequestMessage CreateMessage(HttpMethod method, string path)
        {
            var uri = GetFullUri(path);
            var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", GetBase64Creds());
            return request;
        }

        private string GetBase64Creds()
        {
            return Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Credentials.UserName}:{Credentials.Password}"));
        }

        private Uri GetFullUri(string partialUrl)
        {
            var uri = _Uri.AbsoluteUri;
            if (!uri.EndsWith("/", StringComparison.InvariantCultureIgnoreCase))
                uri += "/";
            return new Uri(uri + partialUrl);
        }

        async Task<LightningInvoice> ILightningInvoiceClient.GetInvoice(string invoiceId, CancellationToken cancellation)
        {
            var invoice = await GetInvoice(invoiceId, cancellation);
            if (invoice == null)
                return null;
            return ChargeClient.ToLightningInvoice(invoice);
        }

        async Task<ILightningListenInvoiceSession> ILightningInvoiceClient.Listen(CancellationToken cancellation)
        {
            return await Listen(cancellation);
        }

        internal static LightningInvoice ToLightningInvoice(ChargeInvoice invoice)
        {
            return new LightningInvoice()
            {
                Id = invoice.Id ?? invoice.Label,
                Amount = invoice.MilliSatoshi,
                BOLT11 = invoice.PaymentRequest,
                PaidAt = invoice.PaidAt,
                Status = invoice.Status
            };
        }

        async Task<LightningInvoice> ILightningInvoiceClient.CreateInvoice(LightMoney amount, string description, TimeSpan expiry, CancellationToken cancellation)
        {
            var invoice = await CreateInvoiceAsync(new CreateInvoiceRequest() { Amount = amount, Expiry = expiry, Description = description ?? "" }, cancellation);
            return new LightningInvoice() { Id = invoice.Id, Amount = amount, BOLT11 = invoice.PayReq, Status = "unpaid" };
        }

        async Task<LightningNodeInformation> ILightningInvoiceClient.GetInfo(CancellationToken cancellation)
        {
            var info = await GetInfoAsync(cancellation);
            return CLightning.CLightningRPCClient.ToLightningNodeInformation(info);
        }
    }
}
