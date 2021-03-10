using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        private readonly string _apiKey;
        private readonly Uri _btcpayHost;
        private readonly string _username;
        private readonly string _password;
        private readonly HttpClient _httpClient;

        public Uri Host => _btcpayHost;

        public string APIKey => _apiKey;

        public BTCPayServerClient(Uri btcpayHost, HttpClient httpClient = null)
        {
            if (btcpayHost == null)
                throw new ArgumentNullException(nameof(btcpayHost));
            _btcpayHost = btcpayHost;
            _httpClient = httpClient ?? new HttpClient();
        }
        public BTCPayServerClient(Uri btcpayHost, string APIKey, HttpClient httpClient = null)
        {
            _apiKey = APIKey;
            _btcpayHost = btcpayHost;
            _httpClient = httpClient ?? new HttpClient();
        }

        public BTCPayServerClient(Uri btcpayHost, string username, string password, HttpClient httpClient = null)
        {
            _apiKey = APIKey;
            _btcpayHost = btcpayHost;
            _username = username;
            _password = password;
            _httpClient = httpClient ?? new HttpClient();
        }

        protected async Task HandleResponse(HttpResponseMessage message)
        {
            if (message.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var err = JsonConvert.DeserializeObject<Models.GreenfieldValidationError[]>(await message.Content.ReadAsStringAsync());
                ;
                throw new GreenFieldValidationException(err);
            }
            else if (message.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var err = JsonConvert.DeserializeObject<Models.GreenfieldAPIError>(await message.Content.ReadAsStringAsync());
                throw new GreenFieldAPIException(err);
            }

            message.EnsureSuccessStatusCode();
        }

        protected async Task<T> HandleResponse<T>(HttpResponseMessage message)
        {
            await HandleResponse(message);
            var str = await message.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<T>(str);
        }

        protected virtual HttpRequestMessage CreateHttpRequest(string path,
            Dictionary<string, object> queryPayload = null,
            HttpMethod method = null)
        {
            UriBuilder uriBuilder = new UriBuilder(_btcpayHost) { Path = path };
            if (queryPayload != null && queryPayload.Any())
            {
                AppendPayloadToQuery(uriBuilder, queryPayload);
            }

            var httpRequest = new HttpRequestMessage(method ?? HttpMethod.Get, uriBuilder.Uri);
            if (_apiKey != null)
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("token", _apiKey);
            else if (!string.IsNullOrEmpty(_username))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", System.Convert.ToBase64String(Encoding.ASCII.GetBytes(_username + ":" + _password)));
            }


            return httpRequest;
        }

        protected virtual HttpRequestMessage CreateHttpRequest<T>(string path,
            Dictionary<string, object> queryPayload = null,
            T bodyPayload = default, HttpMethod method = null)
        {
            var request = CreateHttpRequest(path, queryPayload, method);
            if (typeof(T).IsPrimitive || !EqualityComparer<T>.Default.Equals(bodyPayload, default(T)))
            {
                request.Content = new StringContent(JsonConvert.SerializeObject(bodyPayload), Encoding.UTF8, "application/json");
            }

            return request;
        }

        public static void AppendPayloadToQuery(UriBuilder uri, KeyValuePair<string, object> keyValuePair)
        {
            if (uri.Query.Length > 1)
                uri.Query += "&";

            UriBuilder uriBuilder = uri;
            if (!(keyValuePair.Value is string) &&
                keyValuePair.Value.GetType().GetInterfaces().Contains((typeof(IEnumerable))))
            {
                foreach (var item in (IEnumerable)keyValuePair.Value)
                {
                    uriBuilder.Query = uriBuilder.Query + Uri.EscapeDataString(keyValuePair.Key) + "=" +
                                       Uri.EscapeDataString(item.ToString()) + "&";
                }
            }
            else
            {
                uriBuilder.Query = uriBuilder.Query + Uri.EscapeDataString(keyValuePair.Key) + "=" +
                                   Uri.EscapeDataString(keyValuePair.Value.ToString()) + "&";
            }
            uri.Query = uri.Query.Trim('&');
        }

        public static void AppendPayloadToQuery(UriBuilder uri, Dictionary<string, object> payload)
        {
            if (uri.Query.Length > 1)
                uri.Query += "&";
            foreach (KeyValuePair<string, object> keyValuePair in payload)
            {
                AppendPayloadToQuery(uri, keyValuePair);
            }
        }
    }
}
