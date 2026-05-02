#nullable enable
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Translations
{
    public class LanguagePackUpdateService(IHttpClientFactory httpClientFactory)
    {
        public record LanguageManifestEntry(
            string Name,
            string? Native,
            string? MaintainerHandle,
            string? MaintainerUrl,
            DateTimeOffset? Updated,
            string File,
            string Sha);

        private readonly ConcurrentDictionary<string, (bool UpdateAvailable, DateTime CheckedAt)> _updateCheckCache = new();
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);
        private (JArray Languages, DateTime FetchedAt)? _manifestCache;
        private readonly SemaphoreSlim _manifestLock = new(1, 1);

        private const string ManifestUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/manifest.json";
        private const string RawBaseUrl = "https://raw.githubusercontent.com/btcpayserver/btcpayserver-translator/main/Translator/";

        private async Task<JArray> FetchManifest()
        {
            if (_manifestCache is { } cached && DateTime.UtcNow - cached.FetchedAt < _cacheExpiration)
                return cached.Languages;

            await _manifestLock.WaitAsync();
            try
            {
                if (_manifestCache is { } lockedCached && DateTime.UtcNow - lockedCached.FetchedAt < _cacheExpiration)
                    return lockedCached.Languages;

                using var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                var json = await httpClient.GetStringAsync(ManifestUrl);
                var languages = JObject.Parse(json)["Languages"] as JArray
                    ?? throw new InvalidOperationException("Manifest is missing the 'Languages' array.");
                _manifestCache = (languages, DateTime.UtcNow);
                return languages;
            }
            finally
            {
                _manifestLock.Release();
            }
        }

        private static JObject? FindEntry(JArray languages, string language)
        {
            foreach (var token in languages)
            {
                if (token is JObject entry &&
                    string.Equals(entry["Name"]?.ToString(), language, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        private static LanguageManifestEntry ToManifestEntry(JObject entry)
        {
            var maintainer = entry["Maintainer"]?.ToString();
            string? maintainerHandle = null;
            string? maintainerUrl = null;
            if (!string.IsNullOrEmpty(maintainer))
            {
                var split = maintainer.Split('|', 2);
                maintainerHandle = split[0];
                if (split.Length > 1)
                    maintainerUrl = split[1];
            }

            DateTimeOffset? updated = null;
            var updatedRaw = entry["Updated"]?.ToString();
            if (DateTimeOffset.TryParse(updatedRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedUpdated))
                updated = parsedUpdated;

            return new LanguageManifestEntry(
                entry["Name"]?.ToString() ?? string.Empty,
                entry["Native"]?.ToString(),
                maintainerHandle,
                maintainerUrl,
                updated,
                entry["File"]?.ToString() ?? string.Empty,
                entry["Sha"]?.ToString() ?? string.Empty);
        }

        public async Task<(LanguageManifestEntry[] Languages, bool Degraded)> GetManifestLanguages()
        {
            try
            {
                var languages = await FetchManifest();
                return (languages.OfType<JObject>().Select(ToManifestEntry).Where(e => !string.IsNullOrEmpty(e.Name)).ToArray(), false);
            }
            catch (Exception)
            {
                return ([], true);
            }
        }

        public async Task<string[]> GetAvailableLanguages()
        {
            var (languages, _) = await GetManifestLanguages();
            return languages.Select(l => l.Name).ToArray();
        }

        public async Task<(string translationsJson, string version)> FetchLanguagePackFromRepository(string language)
        {
            var languages = await FetchManifest();
            var entry = FindEntry(languages, language)
                ?? throw new ArgumentException($"Language '{language}' was not found in the manifest.", nameof(language));

            var filePath = entry["File"]?.ToString()
                ?? throw new InvalidOperationException("Manifest entry is missing the 'File' field.");
            var expectedSha = entry["Sha"]?.ToString()
                ?? throw new InvalidOperationException("Manifest entry is missing the 'Sha' field.");

            var httpClient = httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            var translationsBytes = await httpClient.GetByteArrayAsync(RawBaseUrl + filePath);

            var actualSha = Convert.ToHexString(SHA256.HashData(translationsBytes));
            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Downloaded language pack '{language}' SHA-256 mismatch: expected {expectedSha}, got {actualSha}. The download may be corrupt or tampered with.");

            var translationsJson = Encoding.UTF8.GetString(translationsBytes);
            return (translationsJson, expectedSha);
        }

        public async Task<bool> CheckForLanguagePackUpdateCached(string language, JObject metadata)
        {
            if (_updateCheckCache.TryGetValue(language, out var cached) &&
                DateTime.UtcNow - cached.CheckedAt < _cacheExpiration)
                return cached.UpdateAvailable;

            var updateAvailable = await CheckForLanguagePackUpdate(language, metadata);
            _updateCheckCache[language] = (updateAvailable, DateTime.UtcNow);
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
                var languages = await FetchManifest();
                var entry = FindEntry(languages, language);
                if (entry is null)
                    return false;

                var remoteSha = entry["Sha"]?.ToString();
                if (string.IsNullOrEmpty(remoteSha))
                {
                    return false;
                }

                var localVersion = metadata["version"]?.ToString();

                if (string.IsNullOrEmpty(localVersion))
                    return true;

                return remoteSha != localVersion;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
