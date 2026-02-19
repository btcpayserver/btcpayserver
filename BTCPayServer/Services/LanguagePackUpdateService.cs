using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services
{
    public class LanguagePackUpdateService
    {
        private readonly ConcurrentDictionary<string, (bool UpdateAvailable, DateTime CheckedAt)> _updateCheckCache = new();
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);
        private readonly IHttpClientFactory _httpClientFactory;

        public LanguagePackUpdateService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

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
                
                var fileName = Uri.EscapeDataString(language.ToLowerInvariant());
                var url = $"https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/translations/{fileName}.json";
                
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                
                var remoteContent = await httpClient.GetStringAsync(url);
                
                var remoteHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(remoteContent));
                var remoteVersion = Convert.ToHexString(remoteHash);
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
