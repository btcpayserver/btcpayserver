using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Common.Altcoins.Chia.RPC;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Services.Altcoins.Chia.RPC
{
    public class JsonRpcClient
    {
        private readonly Uri _address;
        private readonly HttpClient _httpClient;

        public JsonRpcClient(Uri address, string certPath, string keyPath)
        {
            _address = address;
            var handler = new SocketsHttpHandler();
            handler.SslOptions.ClientCertificates = CertLoader.GetCerts(certPath, keyPath);
            handler.SslOptions.RemoteCertificateValidationCallback += ValidateServerCertificate;
            _httpClient = new HttpClient(handler);
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
            Console.WriteLine(method);

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

        private static bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            return (sslPolicyErrors & SslPolicyErrors.RemoteCertificateNotAvailable) !=
                   SslPolicyErrors.RemoteCertificateNotAvailable;
        }
    }
}
