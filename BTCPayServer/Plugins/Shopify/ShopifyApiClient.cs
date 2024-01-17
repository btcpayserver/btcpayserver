using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Shopify.ApiModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Shopify
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

            var bearer = $"{_credentials.ApiKey}:{_credentials.ApiPassword}";
            bearer = NBitcoin.DataEncoders.Encoders.Base64.EncodeData(Encoding.UTF8.GetBytes(bearer));

            _httpClient.DefaultRequestHeaders.Add("Authorization", "Basic " + bearer);
        }

        private HttpRequestMessage CreateRequest(string shopName, HttpMethod method, string action,
            string relativeUrl = null)
        {
            var url =
                $"https://{(shopName.Contains('.', StringComparison.InvariantCulture) ? shopName : $"{shopName}.myshopify.com")}/{relativeUrl ?? ("admin/api/2020-07/" + action)}";
            var req = new HttpRequestMessage(method, url);
            return req;
        }

        private async Task<string> SendRequest(HttpRequestMessage req)
        {
            using var resp = await _httpClient.SendAsync(req);

            var strResp = await resp.Content.ReadAsStringAsync();
            if (strResp.StartsWith("{", StringComparison.OrdinalIgnoreCase))
            {
                if (JObject.Parse(strResp)["errors"]?.Value<string>() is string error)
                {
                    if (error == "Not Found")
                        error = "Shop not found";
                    throw new ShopifyApiException(error);
                }
            }
            return strResp;
        }

        public async Task<CreateWebhookResponse> CreateWebhook(string topic, string address, string format = "json")
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Post, $"webhooks.json");
            req.Content = new StringContent(JsonConvert.SerializeObject(new { topic, address, format }), Encoding.UTF8,
                "application/json");
            var strResp = await SendRequest(req);

            return JsonConvert.DeserializeObject<CreateWebhookResponse>(strResp);
        }

        public async Task RemoveWebhook(string id)
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Delete, $"webhooks/{id}.json");
            var strResp = await SendRequest(req);
        }

        public async Task<string[]> CheckScopes()
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Get, null, "admin/oauth/access_scopes.json");
            var c = JObject.Parse(await SendRequest(req));
            return c["access_scopes"].Values<JToken>()
                .Select(token => token["handle"].Value<string>()).ToArray();
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

        public async Task<ShopifyOrder> GetOrder(string orderId)
        {
            var req = CreateRequest(_credentials.ShopName, HttpMethod.Get,
                $"orders/{orderId}.json?fields=id,order_number,total_price,total_outstanding,currency,presentment_currency,transactions,financial_status");

            var strResp = await SendRequest(req);

            return JObject.Parse(strResp)["order"].ToObject<ShopifyOrder>();
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
