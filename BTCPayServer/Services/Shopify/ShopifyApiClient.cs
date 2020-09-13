using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
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

        public async Task<dynamic> TransactionsList(string orderId)
        {
            var req = createRequest(_creds.ShopName, HttpMethod.Get, $"orders/{orderId}/transactions.json");

            var strResp = await sendRequest(req);

            dynamic parsed = JObject.Parse(strResp);

            return parsed;
        }

        public async Task<dynamic> TransactionCreate(string orderId, TransactionCreate txnCreate)
        {
            var postJson = JsonConvert.SerializeObject(txnCreate);

            var req = createRequest(_creds.ShopName, HttpMethod.Post, $"orders/{orderId}/transactions.json");
            req.Content = new StringContent(postJson, Encoding.UTF8, "application/json");

            var strResp = await sendRequest(req);
            return JObject.Parse(strResp);
        }

        public async Task<int> OrdersCount()
        {
            var req = createRequest(_creds.ShopName, HttpMethod.Get, $"orders/count.json");
            var strResp = await sendRequest(req);

            dynamic parsed = JObject.Parse(strResp);

            return parsed.count;
        }
    }

    public class ShopifyApiClientCredentials
    {
        public string ShopName { get; set; }
        public string ApiKey { get; set; }
        public string ApiPassword { get; set; }
        public string SharedSecret { get; set; }
    }

    public class TransactionCreate
    {
        public DataHolder transaction { get; set; }

        public class DataHolder
        {
            public string currency { get; set; }
            public string amount { get; set; }
            public string kind { get; set; }
            public string parent_id { get; set; }
            public string gateway { get; set; }
            public string source { get; set; }
        }
    }
}
