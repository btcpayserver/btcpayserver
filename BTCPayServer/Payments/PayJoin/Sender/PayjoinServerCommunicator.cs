using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BIP78.Sender;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.PayJoin.Sender
{
    public class PayjoinServerCommunicator : IPayjoinServerCommunicator
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public const string PayjoinOnionNamedClient = "payjoin.onion";
        public const string PayjoinClearnetNamedClient = "payjoin.clearnet";

        public PayjoinServerCommunicator(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<PSBT> RequestPayjoin(Uri endpoint, PSBT originalTx, CancellationToken cancellationToken)
        {
            using HttpClient client = CreateHttpClient(endpoint);
            var bpuresponse = await client.PostAsync(endpoint,
                new StringContent(originalTx.ToBase64(), Encoding.UTF8, "text/plain"), cancellationToken);
            if (!bpuresponse.IsSuccessStatusCode)
            {
                var errorStr = await bpuresponse.Content.ReadAsStringAsync();
                try
                {
                    var error = JObject.Parse(errorStr);
                    throw new PayjoinReceiverException(error["errorCode"].Value<string>(),
                        error["message"].Value<string>());
                }
                catch (JsonReaderException)
                {
                    // will throw
                    bpuresponse.EnsureSuccessStatusCode();
                    throw;
                }
            }

            var hex = await bpuresponse.Content.ReadAsStringAsync();
            return PSBT.Parse(hex, originalTx.Network);
        }

        private HttpClient CreateHttpClient(Uri uri)
        {
            return _httpClientFactory.CreateClient(uri.IsOnion() ? PayjoinOnionNamedClient : PayjoinClearnetNamedClient);
        }
    }
}
