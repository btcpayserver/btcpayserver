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
            string Sha,
            bool Rtl,
            string? Code, 
            string? Bcp47)
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
                    dto.Sha ?? string.Empty,
                    dto.Rtl ?? false,
                    dto.Code,
                    dto.Bcp47);
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
            string? Sha,
            bool? Rtl,
            string? Code,
            string? Bcp47);

        internal record ManifestRootDto(ManifestLanguageDto[]? Languages, string? Redirect);
        private sealed record ManifestSnapshot(LanguageManifestEntry[] Entries, string BaseUrl);

        private const string ManifestCacheKey = "translations.manifest";
        private const string ManifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";
        private const string TrustedHost = "raw.githubusercontent.com";
        private const string TrustedOrgPath = "/btcpayserver/";

        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

        private async Task<ManifestSnapshot> GetSnapshot()
        {
            if (memoryCache.TryGetValue(ManifestCacheKey, out ManifestSnapshot? cached) && cached is not null)
                return cached;

            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var manifestUrl = ManifestUrl;
            var json = await httpClient.GetStringAsync(manifestUrl);
            var root = JsonConvert.DeserializeObject<ManifestRootDto>(json);
            if (root?.Languages is null)
                throw new InvalidOperationException("Manifest is missing the 'Languages' array.");

            if (!string.IsNullOrEmpty(root.Redirect))
            {
                // Validate the URL as it will be requested. Uri resolves dot segments ("/btcpayserver/../other/"),
                // so a string prefix check on the raw value could be escaped.
                if (!IsTrustedRedirect(root.Redirect, out var redirect))
                    throw new InvalidOperationException($"Manifest redirect '{root.Redirect}' is outside the trusted repository.");

                manifestUrl = redirect.AbsoluteUri;
                json = await httpClient.GetStringAsync(manifestUrl);
                root = JsonConvert.DeserializeObject<ManifestRootDto>(json);
                if (root?.Languages is null)
                    throw new InvalidOperationException("Redirected manifest is missing the 'Languages' array.");
            }
            var entries = root.Languages.Select(LanguageManifestEntry.FromDto)
                .Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();

            var snapshot = new ManifestSnapshot(entries, DeriveBaseUrl(manifestUrl));
            memoryCache.Set(ManifestCacheKey, snapshot, CacheLifetime);
            return snapshot;
        }

        internal static bool IsTrustedRedirect(string value, out Uri redirect)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out redirect!)
                   && redirect.Scheme == Uri.UriSchemeHttps
                   && string.Equals(redirect.Host, TrustedHost, StringComparison.OrdinalIgnoreCase)
                   && redirect.AbsolutePath.StartsWith(TrustedOrgPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string DeriveBaseUrl(string manifestUrl)
        {
            var lastSlash = manifestUrl.LastIndexOf('/');
            return lastSlash < 0 ? manifestUrl : manifestUrl[..(lastSlash + 1)];
        }

        public async Task<LanguageManifestEntry[]> GetManifestLanguages() => (await GetSnapshot()).Entries;

        public async Task<(string translationsJson, string version, bool rtl)> FetchLanguagePackFromRepository(string language)
        {
            var snapshot = await GetSnapshot();
            var entry = snapshot.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, language, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Language '{language}' was not found in the manifest.", nameof(language));

            if (string.IsNullOrEmpty(entry.File))
                throw new InvalidOperationException("Manifest entry is missing the 'File' field.");
            if (string.IsNullOrEmpty(entry.Sha))
                throw new InvalidOperationException("Manifest entry is missing the 'Sha' field.");

            using var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            var translationsBytes = await httpClient.GetByteArrayAsync(snapshot.BaseUrl + entry.File);

            var actualSha = Convert.ToHexString(SHA256.HashData(translationsBytes));
            if (!string.Equals(actualSha, entry.Sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Downloaded language pack '{language}' SHA-256 mismatch: expected {entry.Sha}, got {actualSha}. The download may be corrupt or tampered with.");

            return (Encoding.UTF8.GetString(translationsBytes), entry.Sha, entry.Rtl);
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
                var snapshot = await GetSnapshot();
                var entry = snapshot.Entries.FirstOrDefault(e => string.Equals(e.Name, language, StringComparison.OrdinalIgnoreCase));
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
