using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Services.Shopify.ApiModels;
using DBriize.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Shopify
{
    public class ShopifyApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly ShopifyApiClientCredentials _creds;

        public ShopifyApiClient(IHttpClientFactory httpClientFactory, ILogger logger, ShopifyApiClientCredentials creds)
        {
            if (httpClientFactory != null)
            {
                _httpClient = httpClientFactory.CreateClient(nameof(ShopifyApiClient));
            }
            else // tests don't provide IHttpClientFactory
            {
                _httpClient = new HttpClient();
            }
            _logger = logger;
            _creds = creds;

            var bearer = $"{creds.ApiKey}:{creds.ApiPassword}";
            bearer = Encoding.UTF8.GetBytes(bearer).ToBase64String();

            _httpClient.DefaultRequestHeaders.Add("Authorization", "Basic " + bearer);
        }

        private HttpRequestMessage createRequest(string shopNameInUrl, HttpMethod method, string action)
        {
            var url = $"https://{shopNameInUrl}.myshopify.com/admin/api/2020-07/" + action;

            var req = new HttpRequestMessage(method, url);

            return req;
        }

        private async Task<string> sendRequest(HttpRequestMessage req)
        {
            using var resp = await _httpClient.SendAsync(req);

            var strResp = await resp.Content.ReadAsStringAsync();
            return strResp;
        }

        public async Task<TransactionsListResp> TransactionsList(string orderId)
        {
            var req = createRequest(_creds.ShopName, HttpMethod.Get, $"orders/{orderId}/transactions.json");

            var strResp = await sendRequest(req);

            var parsed = JsonConvert.DeserializeObject<TransactionsListResp>(strResp);

            return parsed;
        }

        public async Task<TransactionsCreateResp> TransactionCreate(string orderId, TransactionsCreateReq txnCreate)
        {
            var postJson = JsonConvert.SerializeObject(txnCreate);

            var req = createRequest(_creds.ShopName, HttpMethod.Post, $"orders/{orderId}/transactions.json");
            req.Content = new StringContent(postJson, Encoding.UTF8, "application/json");

            var strResp = await sendRequest(req);
            return JsonConvert.DeserializeObject<TransactionsCreateResp>(strResp);
        }

        public async Task<long> OrdersCount()
        {
            var req = createRequest(_creds.ShopName, HttpMethod.Get, $"orders/count.json");
            var strResp = await sendRequest(req);

            var parsed = JsonConvert.DeserializeObject<OrdersCountResp>(strResp);

            return parsed.count;
        }

        public async Task<bool> OrderExists(string orderId)
        {
            var req = createRequest(_creds.ShopName, HttpMethod.Get, $"orders/{orderId}.json?fields=id");
            var strResp = await sendRequest(req);

            return strResp?.Contains(orderId, StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    public class ShopifyApiClientCredentials
    {
        public string ShopName { get; set; }
        public string ApiKey { get; set; }
        public string ApiPassword { get; set; }
        public string SharedSecret { get; set; }
    }
}
