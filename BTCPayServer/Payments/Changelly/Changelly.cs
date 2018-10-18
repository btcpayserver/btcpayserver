using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Payments.Changelly.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SshNet.Security.Cryptography;

namespace BTCPayServer.Payments.Changelly
{
    public class Changelly
    {
        private readonly string _apikey;
        private readonly string _apisecret;
        private readonly string _apiurl;
        private readonly HttpClient _httpClient;

        public Changelly(string apiKey, string apiSecret, string apiUrl)
        {
            _apikey = apiKey;
            _apisecret = apiSecret;
            _apiurl = apiUrl;
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(apiUrl);
            _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }


        private static string ToHexString(byte[] array)
        {
            var hex = new StringBuilder(array.Length * 2);
            foreach (var b in array)
            {
                hex.AppendFormat("{0:x2}", b);
            }

            return hex.ToString();
        }

        private async Task<(ChangellyResponse<T> Result, bool Success, string Error)> PostToApi<T>(string message)
        {
            try
            {
                var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_apisecret));
                var hashMessage = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                var sign = ToHexString(hashMessage);

                var request = new HttpRequestMessage(HttpMethod.Post, "");
                request.Headers.Add("sign", sign);


                var result = await _httpClient.SendAsync(request);

                if (!result.IsSuccessStatusCode)
                    return (null, false, result.ReasonPhrase);
                var content =
                    await result.Content.ReadAsStringAsync();
                return (
                    JObject.Parse(content).ToObject<ChangellyResponse<T>>(), true, "");
            }
            catch (Exception Ex)
            {
                return (null, false, Ex.Message);
            }
        }

        public virtual async Task<(IEnumerable<CurrencyFull> Currencies, bool Success, string Error)> GetCurrenciesFull()
        {
            try
            {
                const string message = @"{
		            ""jsonrpc"": ""2.0"",
		            ""id"": 1,
		            ""method"": ""getCurrenciesFull"",
		            ""params"": []
			    }";

                var result = await PostToApi<IEnumerable<CurrencyFull>>(message);
                return !result.Success ? (null, false, result.Error) :
                    (result.Result.Result, true, "");
            }
            catch (Exception Ex)
            {
                return (null, false, Ex.Message);
            }
        }

        public virtual async Task<(double Amount, bool Success, string Error)> GetExchangeAmount(string fromCurrency,
            string toCurrency,
            double amount)
        {
            try
            {
                var message =
                    "{\"id\": \"test\",\"jsonrpc\": \"2.0\",\"method\": \"getExchangeAmount\",\"params\":{\"from\": \"" +
                    fromCurrency + "\",\"to\": \"" + toCurrency + "\",\"amount\": \"" + amount + "\"}}";

                var result = await PostToApi<double>(message);
                return !result.Success ? (0, false, result.Error) :
                    (result.Result.Result, true, "");
            }
            catch (Exception Ex)
            {
                return (0, false, Ex.Message);
            }
        }
    }
}
