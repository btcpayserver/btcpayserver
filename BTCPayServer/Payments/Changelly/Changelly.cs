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

        private async Task<(string result, bool, string)> PostToApi(string message)
        {
            try
            {
                HMACSHA512 hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_apisecret));
                byte[] hashmessage = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                string sign = ToHexString(hashmessage);

                var request = new HttpRequestMessage(HttpMethod.Post, "");
                request.Headers.Add("sign", sign);


                var result = await _httpClient.SendAsync(request);

                if (result.IsSuccessStatusCode)
                {
                    var content =
                        await result.Content.ReadAsStringAsync();
                    return (content, true, "");
                }

                return ("", false, result.ReasonPhrase);
            }
            catch (Exception Ex)
            {
                return ("", false, Ex.Message);
            }
        }

        public async Task<(IList<CurrencyFull> currencyFulls, bool, string)> GetCurrenciesFull()
        {
            try
            {
                const string message = @"{
		            ""jsonrpc"": ""2.0"",
		            ""id"": 1,
		            ""method"": ""getCurrenciesFull"",
		            ""params"": []
			    }";

                List<CurrencyFull> currencyFulls = new List<CurrencyFull>();
                var result = await PostToApi(message);
                if (!result.Item2)
                {
                    return (null, false, result.Item3);
                }

                var response = JsonConvert.DeserializeObject<Response>(result.result);

                if (response.Error != null) return (null, false, response.Error.Message);
                var array = JArray.Parse(response.Result.ToString());
                foreach (var item in array)
                {
                    currencyFulls.Add(item.ToObject<CurrencyFull>());
                }

                return (currencyFulls, true, "");

            }
            catch (Exception Ex)
            {
                return (null, false, Ex.Message);
            }
        }

        public async Task<(double, bool, string)> GetExchangeAmount(string fromCurrency, string toCurrency,
            double amount)
        {
            try
            {
                var message =
                    "{\"id\": \"test\",\"jsonrpc\": \"2.0\",\"method\": \"getExchangeAmount\",\"params\":{\"from\": \"" +
                    fromCurrency + "\",\"to\": \"" + toCurrency + "\",\"amount\": \"" + amount + "\"}}";

                var result = await PostToApi(message);
                if (!result.Item2)
                {
                    return (0, false, result.Item3);
                }

                var response = JsonConvert.DeserializeObject<ExchangeResponse>(result.result);
                return response.Error == null
                    ? (Convert.ToDouble(response.Result), true, "")
                    : (0, false, response.Error.Message);
            }
            catch (Exception Ex)
            {
                return (0, false, Ex.Message);
            }
        }
    }
}
