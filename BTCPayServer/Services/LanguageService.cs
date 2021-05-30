using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Services
{
    public class LanguageService
    {
        private readonly Language[] _languages;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public LanguageService(IWebHostEnvironment environment, IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
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

        public Language FindBestMatch(string defaultLang)
        {
            if (_httpContextAccessor.HttpContext?.Request?.Headers?.TryGetValue("Accept-Language", out var acceptLanguage) is true && !string.IsNullOrEmpty(acceptLanguage))
            {
                var locales = acceptLanguage.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (locales.Length > 0)
                {
                    var firstLocale = locales[0];
                    var parts = firstLocale.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    defaultLang = parts[0];
                }
            }
            
            if (defaultLang is null)
                return null;
            defaultLang = defaultLang.Trim();
            if (defaultLang.Length < 2)
                return null;
            var split = defaultLang.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (split.Length != 1 && split.Length != 2)
                return null;
            var lang = split[0];
            var country = split.Length == 2 ? split[1] : split[0].ToUpperInvariant();

            var langStart = lang + "-";
            var langMatches = GetLanguages()
                .Where(l => l.Code.Equals(lang, StringComparison.OrdinalIgnoreCase) ||
                            l.Code.StartsWith(langStart, StringComparison.OrdinalIgnoreCase));

            var countryMatches = langMatches;
            var countryEnd = "-" + country;
            countryMatches =
                countryMatches
                .Where(l => l.Code.EndsWith(countryEnd, StringComparison.OrdinalIgnoreCase));
            return countryMatches.FirstOrDefault() ?? langMatches.FirstOrDefault();
        }
    }
}
