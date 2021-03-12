using System;
using System.Net.Http;
using BTCPayServer.BIP78.Sender;

namespace BTCPayServer.Payments.PayJoin.Sender
{
    public class PayjoinServerCommunicator : HttpClientPayjoinServerCommunicator
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public const string PayjoinOnionNamedClient = "payjoin.onion";
        public const string PayjoinClearnetNamedClient = "payjoin.clearnet";

        public PayjoinServerCommunicator(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        protected override HttpClient CreateHttpClient(Uri uri)
        {
            return _httpClientFactory.CreateClient(uri.IsOnion() ? PayjoinOnionNamedClient : PayjoinClearnetNamedClient);
        }
    }
}
