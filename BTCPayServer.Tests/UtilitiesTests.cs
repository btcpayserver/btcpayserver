using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using Xunit;
using System.IO;

namespace BTCPayServer.Tests
{
    /// <summary>
    /// This class hold easy to run utilities for dev time
    /// </summary>
    public class UtilitiesTests
    {
        /// <summary>
        /// Download transifex transactions and put them in BTCPayServer\wwwroot\locales
        /// </summary>
        [Trait("Utilities", "Utilities")]
        [Fact]
        public async Task PullTransifexTranslations()
        {
            // 1. Generate an API Token on https://www.transifex.com/user/settings/api/
            // 2. Run "dotnet user-secrets set TransifexAPIToken <youapitoken>"
            var client = new TransifexClient(GetTransifexAPIToken());
            var json = await client.GetTransifexAsync("https://api.transifex.com/organizations/btcpayserver/projects/btcpayserver/resources/enjson/");
            var langs = ((JObject)json["stats"]).Properties().Select(n => n.Name).ToArray();

            var langsDir = Path.Combine(Services.LanguageService.TryGetSolutionDirectoryInfo().FullName, "BTCPayServer", "wwwroot", "locales");

            Task.WaitAll(langs.Select(async l =>
            {
                if (l == "no")
                    return;
                var j = await client.GetTransifexAsync($"https://www.transifex.com/api/2/project/btcpayserver/resource/enjson/translation/{l}/");
                var content = j["content"].Value<string>();
                if (l == "en_US")
                    l = "en";
                if (l == "ne_NP")
                    l = "np_NP";
                if (l == "zh_CN")
                    l = "zh-SP";
                if (l == "kk")
                    l = "kk-KZ";

                var langCode = l.Replace("_", "-");
                var langFile = Path.Combine(langsDir, langCode + ".json");
                var jobj = JObject.Parse(content);
                jobj["code"] = langCode;
                if ((string)jobj["currentLanguage"] == "English")
                    return;
                jobj.AddFirst(new JProperty("NOTICE_WARN", "THIS CODE HAS BEEN AUTOMATICALLY GENERATED FROM TRANSIFEX, IF YOU WISH TO HELP TRANSLATION COME ON THE SLACK http://slack.btcpayserver.org TO REQUEST PERMISSION TO https://www.transifex.com/btcpayserver/btcpayserver/"));
                content = jobj.ToString(Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(Path.Combine(langsDir, langFile), content);
            }).ToArray());
        }

        private static string GetTransifexAPIToken()
        {
            var builder = new ConfigurationBuilder();
            builder.AddUserSecrets("AB0AC1DD-9D26-485B-9416-56A33F268117");
            var config = builder.Build();
            var token = config["TransifexAPIToken"];
            Assert.False(token == null, "TransifexAPIToken is not set.\n 1.Generate an API Token on https://www.transifex.com/user/settings/api/ \n 2.Run \"dotnet user-secrets set TransifexAPIToken <youapitoken>\"");
            return token;
        }
    }

    public class TransifexClient
    {
        public TransifexClient(string apiToken)
        {
            Client = new HttpClient();
            APIToken = apiToken;
        }

        public HttpClient Client { get; }
        public string APIToken { get; }

        public async Task<JObject> GetTransifexAsync(string uri)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, uri);
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Encoders.Base64.EncodeData(Encoding.ASCII.GetBytes($"api:{APIToken}")));
            message.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            var response = await Client.SendAsync(message);
            return await response.Content.ReadAsAsync<JObject>();
        }
    }
}
