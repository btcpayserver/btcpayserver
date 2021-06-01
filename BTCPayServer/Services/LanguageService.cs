using System;
using System.Collections.Generic;
using System.Globalization;
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
    public class Language
    {
        public Language(string code, string displayName)
        {
            DisplayName = displayName;
            Code = code;
        }

        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("currentLanguage")] public string DisplayName { get; set; }
    }

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

        public Language FindLanguageInAcceptLanguageHeader(string acceptLanguageHeader)
        {
            var supportedLangs = GetLanguages();
            IDictionary<string, float> acceptedLocales = new Dictionary<string, float>();
            var locales = acceptLanguageHeader.Split(',', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < locales.Length; i++)
            {
                try
                {
                    var oneLocale = locales[i];
                    var parts = oneLocale.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    var locale = parts[0];
                    var qualityScore = 1.0f;
                    if (parts.Length == 2)
                    {
                        var qualityScorePart = parts[1];
                        if (qualityScorePart.StartsWith("q=", StringComparison.OrdinalIgnoreCase))
                        {
                            qualityScorePart = qualityScorePart.Substring(2);
                            qualityScore = float.Parse(qualityScorePart, CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            // Invalid format, continue with next
                            continue;
                        }
                    }

                    if (!locale.Equals("*", StringComparison.OrdinalIgnoreCase))
                    {
                        acceptedLocales[locale] = qualityScore;
                    }
                }
                catch (System.FormatException e)
                {
                    // Can't use this piece, moving on...
                }
            }

            var sortedAcceptedLocales = from entry in acceptedLocales orderby entry.Value descending select entry;
            foreach (var pair in sortedAcceptedLocales)
            {
                var locale = pair.Key;
                foreach (var oneLang in supportedLangs)
                {
                    var split = locale.Split('-', StringSplitOptions.RemoveEmptyEntries);
                    var lang = split[0];
                    var country = split.Length == 2 ? split[1] : split[0].ToUpperInvariant();

                    var langStart = lang + "-";
                    var langMatches = supportedLangs
                        .Where(l => l.Code.Equals(lang, StringComparison.OrdinalIgnoreCase) ||
                                    l.Code.StartsWith(langStart, StringComparison.OrdinalIgnoreCase));

                    var countryMatches = langMatches;
                    var countryEnd = "-" + country;
                    countryMatches = countryMatches.Where(l =>
                        l.Code.EndsWith(countryEnd, StringComparison.OrdinalIgnoreCase));
                    var bestMatch = countryMatches.FirstOrDefault() ?? langMatches.FirstOrDefault();

                    if (bestMatch != null)
                    {
                        return bestMatch;
                    }
                }
            }

            return null;
        }

        public Language FindBestMatch(string defaultLang)
        {
            if (_httpContextAccessor.HttpContext?.Request?.Headers?.TryGetValue("Accept-Language",
                out var acceptLanguage) is true && !string.IsNullOrEmpty(acceptLanguage))
            {
                return FindLanguageInAcceptLanguageHeader(acceptLanguage.ToString());
            }

            var supportedLangs = GetLanguages();
            var defaultLanguage = supportedLangs
                .Where(l => l.Code.StartsWith(defaultLang, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            return defaultLanguage;
        }
    }
}
