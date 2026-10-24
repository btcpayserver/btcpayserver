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
        public const string HttpClientName = "BTCPayServer.Plugins.Translations";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

        private async Task<ManifestSnapshot> GetSnapshot()
        {
            if (memoryCache.TryGetValue(ManifestCacheKey, out ManifestSnapshot? cached) && cached is not null)
                return cached;

            var manifestUri = new Uri(ManifestUrl);
            var root = ParseManifest(await GetTrusted(manifestUri));
            if (!string.IsNullOrEmpty(root.Redirect))
            {
                if (!IsTrustedRedirect(root.Redirect, out manifestUri))
                    throw new InvalidOperationException($"Manifest redirect '{root.Redirect}' is outside the trusted repository.");

                root = ParseManifest(await GetTrusted(manifestUri));
            }
            var entries = root.Languages!.Select(LanguageManifestEntry.FromDto)
                .Where(e => !string.IsNullOrEmpty(e.Name)).ToArray();

            var snapshot = new ManifestSnapshot(entries, DeriveBaseUrl(manifestUri.AbsoluteUri));
            memoryCache.Set(ManifestCacheKey, snapshot, CacheLifetime);
            return snapshot;
        }

        private static ManifestRootDto ParseManifest(byte[] body) =>
            JsonConvert.DeserializeObject<ManifestRootDto>(Encoding.UTF8.GetString(body)) is { Languages: not null } root
            ? root : throw new InvalidOperationException("Manifest is missing the 'Languages' array.");

        private async Task<byte[]> GetTrusted(Uri uri)
        {
            if (!IsTrustedUri(uri))
                throw new InvalidOperationException($"'{uri}' is outside the trusted repository.");

            using var client = httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(30);
            using var response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }

        internal static bool IsTrustedRedirect(string value, out Uri redirect) =>
            Uri.TryCreate(value, UriKind.Absolute, out redirect!) && IsTrustedUri(redirect);

        internal static bool IsTrustedUri(Uri uri) => uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, TrustedHost, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(TrustedOrgPath, StringComparison.OrdinalIgnoreCase);

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

            var translationsBytes = await GetTrusted(new Uri(snapshot.BaseUrl + entry.File, UriKind.Absolute));
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
