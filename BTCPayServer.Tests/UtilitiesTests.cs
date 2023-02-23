using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Amazon.Runtime.Internal;
using BTCPayServer.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using Xunit;

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
        [FactWithSecret("TransifexAPIToken")]
        [Trait("Utilities", "Utilities")]
        public async Task PullTransifexTranslations()
        {
            // 1. Generate an API Token on https://www.transifex.com/user/settings/api/
            // 2. Run "dotnet user-secrets set TransifexAPIToken <youapitoken>"
            var client = new TransifexClient(FactWithSecretAttribute.GetFromSecrets("TransifexAPIToken"));

            var proj = "o:btcpayserver:p:btcpayserver";
            var resource = $"{proj}:r:enjson";
            var json = await client.GetTransifexAsync($"https://rest.api.transifex.com/resource_language_stats?filter[project]={proj}&filter[resource]={resource}");
            var langs = json["data"].Select(o => o["id"].Value<string>().Split(':').Last()).ToArray();
            json = await client.GetTransifexAsync($"https://rest.api.transifex.com/resource_strings?filter[resource]={resource}");
            var hashToKeys = json["data"]
                .ToDictionary(
                o => o["id"].Value<string>().Split(':').Last(),
                o => o["attributes"]["key"].Value<string>().Replace("\\.", "."));

            var translations = new ConcurrentDictionary<string, JObject>();
            translations.TryAdd("en",
                new JObject(
                    json["data"]
                    .Select(o => new JProperty(
                        o["attributes"]["key"].Value<string>().Replace("\\.", "."),
                        o["attributes"]["strings"]["other"].Value<string>())))
                );

            var langsDir = Path.Combine(TestUtils.TryGetSolutionDirectoryInfo().FullName, "BTCPayServer", "wwwroot", "locales");

            Task.WaitAll(langs.Select(async l =>
            {
                if (l == "en")
                    return;
                retry:
                var j = await client.GetTransifexAsync($"https://rest.api.transifex.com/resource_translations?filter[resource]={resource}&filter[language]=l:{l}");
                try
                {
                    var jobj = new JObject(
                        j["data"].Select(o => (Key: hashToKeys[o["id"].Value<string>().Split(':')[^3]], Strings: o["attributes"]["strings"]))
                        .Select(o =>
                        new JProperty(
                            o.Key,
                            o.Strings.Type == JTokenType.Null ? translations["en"][o.Key].Value<string>() : o.Strings["other"].Value<string>()
                            )));
                    if (l == "ne_NP")
                        l = "np_NP";
                    if (l == "zh_CN")
                        l = "zh-SP";
                    if (l == "kk")
                        l = "kk-KZ";

                    var langCode = l.Replace("_", "-");
                    jobj["code"] = langCode;

                    if ((string)jobj["currentLanguage"] == "English")
                        return; // Not translated
                    if ((string)jobj["currentLanguage"] == "disable")
                        return; // Not translated

                    if (jobj["InvoiceExpired_Body_3"].Value<string>() == translations["en"]["InvoiceExpired_Body_3"].Value<string>())
                    {
                        jobj["InvoiceExpired_Body_3"] = string.Empty;
                    }
                    translations.TryAdd(langCode, jobj);
                }
                catch
                {
                    goto retry;
                }
            }).ToArray());

            foreach (var t in translations)
            {
                t.Value.AddFirst(new JProperty("NOTICE_WARN", "THIS CODE HAS BEEN AUTOMATICALLY GENERATED FROM TRANSIFEX, IF YOU WISH TO HELP TRANSLATION COME ON THE SLACK http://slack.btcpayserver.org TO REQUEST PERMISSION TO https://www.transifex.com/btcpayserver/btcpayserver/"));
                var content = t.Value.ToString(Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(Path.Combine(langsDir, $"{t.Key}.json"), content);
            }
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
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", APIToken);
            message.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
            var response = await Client.SendAsync(message);
            var str = await response.Content.ReadAsStringAsync();
            return JObject.Parse(str);
        }
    }
}
