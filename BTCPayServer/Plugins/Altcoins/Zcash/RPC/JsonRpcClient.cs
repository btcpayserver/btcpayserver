using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Services.Altcoins.Zcash.RPC
{
    public class JsonRpcClient
    {
        private readonly Uri _address;
        private readonly string _username;
        private readonly string _password;
        private readonly HttpClient _httpClient;

        public JsonRpcClient(Uri address, string username, string password, HttpClient client = null)
        {
            _address = address;
            _username = username;
            _password = password;
            _httpClient = client ?? new HttpClient();
        }


        public async Task<TResponse> SendCommandAsync<TRequest, TResponse>(string method, TRequest data,
            CancellationToken cts = default(CancellationToken))
        {
            var jsonSerializer = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
            var httpRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(_address, method),
                Content = new StringContent(
                    JsonConvert.SerializeObject(data, jsonSerializer),
                    Encoding.UTF8, "application/json")
            };
            // httpRequest.Headers.Accept.Clear();
            // httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            //     Convert.ToBase64String(Encoding.Default.GetBytes($"{_username}:{_password}")));

            var rawResult = await _httpClient.SendAsync(httpRequest, cts);

            var rawJson = await rawResult.Content.ReadAsStringAsync();
            rawResult.EnsureSuccessStatusCode();
            var response = JsonConvert.DeserializeObject<TResponse>(rawJson, jsonSerializer);
            return response;
        }

        public class NoRequestModel
        {
            public static NoRequestModel Instance = new NoRequestModel();
        }

        internal class JsonRpcApiException : Exception
        {
            public JsonRpcResultError Error { get; set; }

            public override string Message => Error?.Message;
        }

        public class JsonRpcResultError
        {
            [JsonProperty("code")] public int Code { get; set; }
            [JsonProperty("message")] public string Message { get; set; }
            [JsonProperty("data")] dynamic Data { get; set; }
        }
        internal class JsonRpcResult<T>
        {


            [JsonProperty("result")] public T Result { get; set; }
            [JsonProperty("error")] public JsonRpcResultError Error { get; set; }
            [JsonProperty("id")] public string Id { get; set; }
        }

        internal class JsonRpcCommand<T>
        {
            [JsonProperty("jsonRpc")] public string JsonRpc { get; set; } = "2.0";
            [JsonProperty("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
            [JsonProperty("method")] public string Method { get; set; }

            [JsonProperty("params")] public T Parameters { get; set; }

            public JsonRpcCommand()
            {
            }

            public JsonRpcCommand(string method, T parameters)
            {
                Method = method;
                Parameters = parameters;
            }
        }
    }
}
