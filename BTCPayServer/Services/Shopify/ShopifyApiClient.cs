using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Services.Shopify.ApiModels;
using DBriize.Utils;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Shopify
{
    public class ShopifyApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly ShopifyApiClientCredentials _credentials;

        public ShopifyApiClient(IHttpClientFactory httpClientFactory, ShopifyApiClientCredentials credentials)
        {
            if (httpClientFactory != null)
            {
                _httpClient = httpClientFactory.CreateClient(nameof(ShopifyApiClient));
            }
            else // tests don't provide IHttpClientFactory
            {
                _httpClient = new HttpClient();
            }
            _credentials = credentials;

            var bearer = $"{credentials.ApiKey}:{credentials.ApiPassword}";
            bearer = Encoding.UTF8.GetBytes(bearer).ToBase64String();

            _httpClient.DefaultRequestHeaders.Add("Authorization", "Basic " + bearer);
        }

        private HttpRequestMessage CreateRequest(string shopName, HttpMethod method, string action)
        {
            var url = $"https://{(shopName.Contains(".", StringComparison.InvariantCulture)? shopName: $"{shopName}.myshopify.com")}/admin/api/2020-07/" + action;
            var req = new HttpRequestMessage(method, url);
            return req;
        }

        private async Task<string> SendRequest(HttpRequestMessage req)
        {
            using var resp = await _httpClient.SendAsync(req);

            var strResp = await resp.Content.ReadAsStringAsync();
            return strResp;
        }
        
        public async Task<CreateWebhookResponse> CreateWebhook(string topic, string address, string format = "json")
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Post, $"webhooks.json");
            req.Content = new StringContent(JsonConvert.SerializeObject(new
            {
                topic,
                address,
                format
            }), Encoding.UTF8, "application/json");
            var strResp = await SendRequest(req);

            return JsonConvert.DeserializeObject<CreateWebhookResponse>(strResp);
        }

        public async Task RemoveWebhook(string id)
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Delete, $"webhooks/{id}.json");
            await SendRequest(req);
        }

        public async Task<CreateScriptResponse> CreateScript(string scriptUrl, string evt = "onload", string scope = "order_status")
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Post, $"script_tags.json");
            req.Content = new StringContent(JsonConvert.SerializeObject(new
            {
                @event = evt,
                src = scriptUrl,
                display_scope = scope
            }), Encoding.UTF8, "application/json");
            var strResp = await SendRequest(req);

            return JsonConvert.DeserializeObject<CreateScriptResponse>(strResp);
        }

        public async Task RemoveScript(string id)
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Delete, $"script_tags/{id}.json");
            await SendRequest(req);
        }

        public async Task<TransactionsListResp> TransactionsList(string orderId)
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Get, $"orders/{orderId}/transactions.json");

            var strResp = await SendRequest(req);

            var parsed = JsonConvert.DeserializeObject<TransactionsListResp>(strResp);

            return parsed;
        }

        public async Task<TransactionsCreateResp> TransactionCreate(string orderId, TransactionsCreateReq txnCreate)
        {
            var postJson = JsonConvert.SerializeObject(txnCreate);

            var req = CreateRequest(_credentials.ShopName, HttpMethod.Post, $"orders/{orderId}/transactions.json");
            req.Content = new StringContent(postJson, Encoding.UTF8, "application/json");

            var strResp = await SendRequest(req);
            return JsonConvert.DeserializeObject<TransactionsCreateResp>(strResp);
        }

        public async Task<long> OrdersCount()
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Get, $"orders/count.json");
            var strResp = await SendRequest(req);

            var parsed = JsonConvert.DeserializeObject<CountResponse>(strResp);

            return parsed.Count;
        }

        public async Task<bool> OrderExists(string orderId)
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Get, $"orders/{orderId}.json?fields=id");
            var strResp = await SendRequest(req);

            return strResp?.Contains(orderId, StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    public class ShopifyApiClientCredentials
    {
        public string ShopName { get; set; }
        public string ApiKey { get; set; }
        public string ApiPassword { get; set; }
    }
}
