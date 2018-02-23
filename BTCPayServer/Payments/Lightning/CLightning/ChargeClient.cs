using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning.CLightning
{
    public class ChargeClient
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
                throw new ArgumentException(paramName:nameof(uri), message:"User information not present in uri");
            var userInfo = uri.UserInfo.Split(':');
            if(userInfo.Length != 2)
                throw new ArgumentException(paramName: nameof(uri), message: "User information not present in uri");
            Credentials = new NetworkCredential(userInfo[0], userInfo[1]);
        }

        public NetworkCredential Credentials { get; set; }

        public GetInfoResponse GetInfo()
        {
            return GetInfoAsync().GetAwaiter().GetResult();
        }
        public async Task<GetInfoResponse> GetInfoAsync()
        {
            var request = Get("info");
            var message = await _Client.SendAsync(request);
            message.EnsureSuccessStatusCode();
            var content = await message.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<GetInfoResponse>(content);
        }

        private HttpRequestMessage Get(string path)
        {
            var uri = GetFullUri(path);
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Credentials.UserName}:{Credentials.Password}")));
            return request;
        }

        private Uri GetFullUri(string partialUrl)
        {
            var uri = _Uri.AbsoluteUri;
            if (!uri.EndsWith("/", StringComparison.InvariantCultureIgnoreCase))
                uri += "/";
            return new Uri(uri + partialUrl);
        }
    }
}
