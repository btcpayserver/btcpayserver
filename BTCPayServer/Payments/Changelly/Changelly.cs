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
        private readonly string _apisecret;
        private readonly bool _showFiat;
        private readonly HttpClient _httpClient;

        public Changelly(string apiKey, string apiSecret, string apiUrl, bool showFiat = true)
        {
            _apisecret = apiSecret;
            _showFiat = showFiat;
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
                request.Content = new StringContent(message, Encoding.UTF8, "application/json");

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

        public virtual async Task<(IEnumerable<CurrencyFull> Currencies, bool Success, string Error)>
            GetCurrenciesFull()
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
                if (!result.Success)
                    return (null, false, result.Error);
                else
                {
                    var appendedResult = _showFiat
                        ? result.Result.Result.Concat(new[]
                        {
                            new CurrencyFull()
                            {
                                Enable = true,
                                Name = "EUR",
                                FullName = "Euro",
                                PayInConfirmations = 0,
                                ImageLink = "https://changelly.com/api/coins/eur.png" 
                            },
                            new CurrencyFull()
                            {
                                Enable = true,
                                Name = "USD",
                                FullName = "US Dollar",
                                PayInConfirmations = 0,
                                ImageLink = "https://changelly.com/api/coins/usd.png" 
                            }
                        })
                        : result.Result.Result;
                    return (appendedResult, true, "");
                }
            }
            catch (Exception Ex)
            {
                return (null, false, Ex.Message);
            }
        }

        public virtual async Task<(decimal Amount, bool Success, string Error)> GetExchangeAmount(string fromCurrency,
            string toCurrency,
            decimal amount)
        {
            try
            {
                var message =
                    "{\"id\": \"test\",\"jsonrpc\": \"2.0\",\"method\": \"getExchangeAmount\",\"params\":{\"from\": \"" +
                    fromCurrency + "\",\"to\": \"" + toCurrency + "\",\"amount\": \"" + amount + "\"}}";

                var result = await PostToApi<string>(message);
                return !result.Success ? (0, false, result.Error) : (Convert.ToDecimal(result.Result.Result), true, "");
            }
            catch (Exception Ex)
            {
                return (0, false, Ex.Message);
            }
        }
    }
}
