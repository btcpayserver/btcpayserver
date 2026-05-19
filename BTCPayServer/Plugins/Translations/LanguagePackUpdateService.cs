#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Translations
{
    public class LanguagePackUpdateService(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache)
    {
        public record LanguageManifestEntry(
            string Name,
            string? Native,
            string? MaintainerHandle,
            string? MaintainerUrl,
            DateTimeOffset? Updated,
            string File,
            string Sha)
        {
            internal static LanguageManifestEntry FromDto(ManifestLanguageDto dto)
            {
                var (handle, url) = SplitMaintainer(dto.Maintainer);
                DateTimeOffset? updated = null;
                if (DateTimeOffset.TryParse(dto.Updated, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var parsed))
                    updated = parsed;
                return new LanguageManifestEntry(
                    dto.Name ?? string.Empty,
                    dto.Native,
                    handle,
                    url,
                    updated,
                    dto.File ?? string.Empty,
                    dto.Sha ?? string.Empty);
            }

            private static (string? Handle, string? Url) SplitMaintainer(string? raw)
            {
                if (string.IsNullOrEmpty(raw)) return (null, null);
                var split = raw.Split('|', 2);
                return (split[0], split.Length > 1 ? split[1] : null);
            }
        }

        internal record ManifestLanguageDto(
            string? Name,
            string? Native,
            string? Maintainer,
            string? Updated,
            string? File,
            string? Sha);

        internal record ManifestRootDto(ManifestLanguageDto[]? Languages);

        private const string ManifestCacheKey = "translations.manifest";
        private const string ManifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";
        private const string RawBaseUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

        private async Task<LanguageManifestEntry[]> GetEntries()
        {
            if (memoryCache.TryGetValue(ManifestCacheKey, out LanguageManifestEntry[]? cached) && cached is not null)
                return cached;

            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            var json = await httpClient.GetStringAsync(ManifestUrl);

            var root = JsonConvert.DeserializeObject<ManifestRootDto>(json);
            if (root?.Languages is null)
                throw new InvalidOperationException("Manifest is missing the 'Languages' array.");

            var entries = root.Languages
                .Select(LanguageManifestEntry.FromDto)
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .ToArray();

            memoryCache.Set(ManifestCacheKey, entries, CacheLifetime);
            return entries;
        }

        public Task<LanguageManifestEntry[]> GetManifestLanguages() => GetEntries();

        public async Task<(string translationsJson, string version)> FetchLanguagePackFromRepository(string language)
        {
            var entries = await GetEntries();
            var entry = entries.FirstOrDefault(e =>
                string.Equals(e.Name, language, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Language '{language}' was not found in the manifest.", nameof(language));

            if (string.IsNullOrEmpty(entry.File))
                throw new InvalidOperationException("Manifest entry is missing the 'File' field.");
            if (string.IsNullOrEmpty(entry.Sha))
                throw new InvalidOperationException("Manifest entry is missing the 'Sha' field.");

            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            var translationsBytes = await httpClient.GetByteArrayAsync(RawBaseUrl + entry.File);

            var actualSha = Convert.ToHexString(SHA256.HashData(translationsBytes));
            if (!string.Equals(actualSha, entry.Sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Downloaded language pack '{language}' SHA-256 mismatch: expected {entry.Sha}, got {actualSha}. The download may be corrupt or tampered with.");

            return (Encoding.UTF8.GetString(translationsBytes), entry.Sha);
        }

        private static string UpdateCacheKey(string language) => $"translations.update.{language}";

        public async Task<bool> CheckForLanguagePackUpdateCached(string language, JObject metadata)
        {
            if (memoryCache.TryGetValue<bool>(UpdateCacheKey(language), out var cached))
                return cached;

            var updateAvailable = await CheckForLanguagePackUpdate(language, metadata);
            memoryCache.Set(UpdateCacheKey(language), updateAvailable, CacheLifetime);
            return updateAvailable;
        }

        public void InvalidateCache(string language)
        {
            memoryCache.Remove(UpdateCacheKey(language));
        }

        private async Task<bool> CheckForLanguagePackUpdate(string language, JObject metadata)
        {
            try
            {
                var entries = await GetEntries();
                var entry = entries.FirstOrDefault(e =>
                    string.Equals(e.Name, language, StringComparison.OrdinalIgnoreCase));
                if (entry is null || string.IsNullOrEmpty(entry.Sha))
                    return false;

                var localVersion = metadata["version"]?.ToString();
                if (string.IsNullOrEmpty(localVersion))
                    return true;

                return !string.Equals(entry.Sha, localVersion, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (
                ex is HttpRequestException
                || ex is TaskCanceledException
                || ex is InvalidOperationException
                || ex is JsonException)
            {
                return false;
            }
        }
    }
}
