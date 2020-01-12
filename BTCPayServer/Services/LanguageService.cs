using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Microsoft.AspNetCore.Hosting;

namespace BTCPayServer.Services
{
    public class Language
    {
        public Language(string code, string displayName)
        {
            DisplayName = displayName;
            Code = code;
        }

        [JsonProperty("code")]
        public string Code { get; set; }
        [JsonProperty("currentLanguage")]
        public string DisplayName { get; set; }
    }

    public class LanguageService
    {
        private readonly Language[] _languages;

        public LanguageService(IWebHostEnvironment environment)
        {
            var path = environment.WebRootPath;
            path = Path.Combine(path, "locales");
            var files = Directory.GetFiles(path, "*.json");
            var result = new List<Language>();
            foreach (var file in files)
            {
                using (var stream = new StreamReader(file))
                {
                    var json = stream.ReadToEnd();
                    result.Add(JObject.Parse(json).ToObject<Language>());
                }
            }

            _languages = result.ToArray();
        }
        public Language[] GetLanguages()
        {
            return _languages;
        }
    }
}
