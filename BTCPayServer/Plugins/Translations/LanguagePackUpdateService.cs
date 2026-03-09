using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Translations
{
    public class LanguagePackUpdateService(IHttpClientFactory httpClientFactory)
    {
        private readonly ConcurrentDictionary<string, (bool UpdateAvailable, DateTime CheckedAt)> _updateCheckCache = new();
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);

        public static string[] GetDownloadableLanguages()
        {
            return new[]
            {
                "Dutch",
                "French",
                "German",
                "Hindi",
                "Indonesian",
                "Italian",
                "Japanese",
                "Norwegian",
                "Korean",
                "Portuguese (Brazil)",
                "Russian",
                "Serbian",
                "Spanish",
                "Thai",
                "Turkish"
            };
        }

        public async Task<(string translationsJson, string version)> FetchLanguagePackFromRepository(string language)
        {
            if (!GetDownloadableLanguages().Contains(language))
            {
                throw new ArgumentException($"Language '{language}' is not a valid downloadable language pack.", nameof(language));
            }

            var fileName = Uri.EscapeDataString(language.ToLowerInvariant());
            var url = $"https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/translations/{fileName}.json";

            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var translationsJson = await httpClient.GetStringAsync(url);

            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(translationsJson));
            var version = Convert.ToHexString(hash);

            return (translationsJson, version);
        }

        public async Task<bool> CheckForLanguagePackUpdateCached(string language, JObject metadata)
        {
            var cacheKey = language;

            if (_updateCheckCache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow - cached.CheckedAt < _cacheExpiration)
                {
                    return cached.UpdateAvailable;
                }
            }

            var updateAvailable = await CheckForLanguagePackUpdate(language, metadata);
            _updateCheckCache[cacheKey] = (updateAvailable, DateTime.UtcNow);

            return updateAvailable;
        }

        public void InvalidateCache(string language)
        {
            _updateCheckCache.TryRemove(language, out _);
        }

        private async Task<bool> CheckForLanguagePackUpdate(string language, JObject metadata)
        {
            try
            {
                if (!GetDownloadableLanguages().Contains(language))
                {
                    return false;
                }


                var (_, remoteVersion) = await FetchLanguagePackFromRepository(language);
                var localVersion = metadata["version"]?.ToString();

                if (string.IsNullOrEmpty(localVersion))
                    return true;

                return remoteVersion != localVersion;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }
    }
}
