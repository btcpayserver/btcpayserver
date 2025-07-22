using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;

namespace BTCPayServer.Services
{
    public class DynamicDnsSettings
    {
        public List<DynamicDnsService> Services { get; set; } = new List<DynamicDnsService>();
    }
    public class DynamicDnsService
    {
        [Display(Name = "Url of the Dynamic DNS service you are using")]
        [Required]
        public string ServiceUrl { get; set; }
        public string Login { get; set; }
        [DataType(DataType.Password)]
        public string Password { get; set; }
        [Display(Name = "Your dynamic DNS hostname")]
        [Required]
        public string Hostname { get; set; }
        public bool Enabled { get; set; } = true;
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? LastUpdated { get; set; }

        public async Task<string> SendUpdateRequest(HttpClient httpClient)
        {
            string errorMessage = null;
            try
            {
                using var request = CreateUpdateRequest();
                var result = await httpClient.SendAsync(request);
                if (!result.IsSuccessStatusCode)
                {
                    try
                    {
                        errorMessage = await result.Content.ReadAsStringAsync();
                    }
                    catch { }
                    errorMessage = $"Error: Invalid return code {result.StatusCode}, expected 200 ({errorMessage.Trim()}) for hostname '{Hostname}'";
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Error: While querying the Dynamic DNS service ({ex.Message}) for hostname '{Hostname}'";
            }
            return errorMessage;
        }
        public HttpRequestMessage CreateUpdateRequest()
        {
            HttpRequestMessage webRequest = new HttpRequestMessage();
            if (!Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var uri) || uri.HostNameType == UriHostNameType.Unknown)
            {
                throw new FormatException($"Invalid service url");
            }

            var builder = new UriBuilder(uri);
            if (!string.IsNullOrEmpty(Login))
            {
                builder.UserName = Login;
            }
            if (!string.IsNullOrEmpty(Password))
            {
                builder.Password = Password;
            }
            builder.UserName = builder.UserName ?? string.Empty;
            builder.Password = builder.Password ?? string.Empty;
            builder.Query = $"hostname={Hostname}";
            webRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Encoders.Base64.EncodeData(new UTF8Encoding(false).GetBytes($"{builder.UserName}:{builder.Password}")));
            webRequest.Headers.TryAddWithoutValidation("User-Agent", $"BTCPayServer/{GetVersion()} btcpayserver@gmail.com");
            webRequest.Method = HttpMethod.Get;
            builder.UserName = string.Empty;
            builder.Password = string.Empty;
            webRequest.RequestUri = builder.Uri;
            return webRequest;
        }

        private string GetVersion()
        {
            return typeof(BTCPayServerEnvironment).GetTypeInfo().Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
        }
    }
}
