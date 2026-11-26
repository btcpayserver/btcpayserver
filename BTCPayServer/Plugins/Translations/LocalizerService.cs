#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Translations
{
    public class InMemoryDefaultTranslationProvider(KeyValuePair<string, string?>[] values) : IDefaultTranslationProvider
    {
        public Task<KeyValuePair<string, string?>[]> GetDefaultTranslations()
        {
            return Task.FromResult(values);
        }
    }
    public class LocalizerService(
        ILogger<LocalizerService> logger,
        ApplicationDbContextFactory contextFactory,
        ISettingsAccessor<PoliciesSettings> settingsAccessor,
        IEnumerable<IDefaultTranslationProvider> defaultTranslationProviders,
        IMemoryCache memoryCache)
    {
        public record LoadedTranslations(Translations Translations, Translations Fallback, string LangName, LanguageMetadata LanguageMetadata);
        LoadedTranslations _LoadedTranslations = new(Translations.Default, Translations.Default, Translations.DefaultLanguage, new("en", false));
        readonly object _loadLock = new();
        long _loadGeneration;

        static readonly TimeSpan CacheIdle = TimeSpan.FromMinutes(30);
        static readonly MemoryCacheEntryOptions UserChoiceOptions = new() { SlidingExpiration = CacheIdle };
        static string GetTranslationsCacheKey(string name) => $"{nameof(LocalizerService)}-translations-{name}";
        static string GetUserChoiceCacheKey(string userId) => $"{nameof(LocalizerService)}-user-{userId}";

        CancellationTokenSource _translationsChanged = new();
        public LoadedTranslations ServerTranslations => _LoadedTranslations;
        public Translations Translations => _LoadedTranslations.Translations;
        public string ServerLanguage => _LoadedTranslations.LangName;

        public void SetUserLanguage(string userId, string? translationName) => memoryCache.Set(GetUserChoiceCacheKey(userId), translationName, UserChoiceOptions);

        /// <summary>
        /// Load the translation of the server into memory
        /// </summary>
        /// <returns></returns>
        public async Task Load()
        {
            long generation;
            lock (_loadLock)
                generation = ++_loadGeneration;
            try
            {
                var translations = await GetTranslations(settingsAccessor.Settings.LangTranslation);
                lock (_loadLock)
                {
                    // A newer reload may already have published updated metadata.
                    if (generation == _loadGeneration)
                        _LoadedTranslations = translations;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load translations");
                throw;
            }
        }

        public static bool IsInstalledTranslation(string translationName, IEnumerable<Translation> installed)
        {
            return installed.Any(t => t.TranslationName == translationName);
        }

        public async Task<LoadedTranslations?> GetUserTranslations(string? translationName)
        {
            if (string.IsNullOrEmpty(translationName) || translationName == _LoadedTranslations.LangName)
                return null;

            var key = GetTranslationsCacheKey(translationName);
            var loading = memoryCache.GetOrCreate(key, async entry =>
            {
                entry.SlidingExpiration = CacheIdle;
                entry.AddExpirationToken(new CancellationChangeToken(_translationsChanged.Token));
                return await GetTranslation(translationName) is null ? null : await GetTranslations(translationName);
            })!;
            try
            {
                return await loading;
            }
            catch
            {
                if (memoryCache.TryGetValue(key, out Task<LoadedTranslations?>? current) && ReferenceEquals(current, loading))
                    memoryCache.Remove(key);
                throw;
            }
        }

        public async Task<LoadedTranslations?> GetTranslationsForUser(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
        {
            var userId = userManager.GetUserId(principal);
            if (userId is null)
                return null;
            var key = GetUserChoiceCacheKey(userId);
            if (!memoryCache.TryGetValue(key, out string? choice))
            {
                var stored = (await userManager.FindByIdAsync(userId))?.GetBlob()?.LangTranslation;
                choice = memoryCache.GetOrCreate(key, e => { e.SlidingExpiration = CacheIdle; return stored; });
            }
            return await GetUserTranslations(choice);
        }
        void InvalidateUserTranslations() => Interlocked.Exchange(ref _translationsChanged, new CancellationTokenSource()).Cancel();

        async Task RefreshTranslations()
        {
            InvalidateUserTranslations();
            // The server translation can itself inherit from the changed dictionary.
            await Load();
        }

        static IEnumerable<Translation> FallbackChain(string? name, Translation[] dictionaries)
        {
            var byName = dictionaries.ToDictionary(t => t.TranslationName);
            var visited = new HashSet<string>();
            while (name is not null && visited.Add(name) && byName.TryGetValue(name, out var dictionary))
            {
                yield return dictionary;
                name = dictionary.Fallback;
            }
        }

        // Resolved metadata always has a language code and direction.
        public static LanguageMetadata ResolveMetadata(string? translationName, Translation[] dictionaries) =>
            ResolveMetadata(FallbackChain(translationName, dictionaries));

        static LanguageMetadata ResolveMetadata(IEnumerable<Translation> chain)
        {
            string? code = null;
            bool? rtl = null;
            foreach (var dictionary in chain)
            {
                var metadata = dictionary.LanguageMetadata;
                code ??= string.IsNullOrWhiteSpace(metadata.Code) ? null : metadata.Code.Trim();
                rtl ??= metadata.Rtl;
            }
            return new LanguageMetadata(code ?? "en", rtl ?? false);
        }

        public async Task<LoadedTranslations> GetTranslations(string? translationName)
        {
            await using var ctx = contextFactory.CreateContext();
            return await GetTranslations(translationName, ctx.Database.GetDbConnection());
        }

        async Task<LoadedTranslations> GetTranslations(string? translationName, DbConnection conn, DbTransaction? transaction = null)
        {
            translationName = string.IsNullOrEmpty(translationName) ? Translations.DefaultLanguage : translationName;
            var chain = FallbackChain(translationName, await GetTranslations(conn, transaction)).ToArray();
            var metadata = ResolveMetadata(chain);
            // Traverse locally rather than using the recursive SQL view, which
            // cannot terminate if a malformed dictionary chain contains a cycle.
            var all = (await conn.QueryAsync<(string dict_id, string sentence, string? translation)>(
                "SELECT dict_id, sentence, translation FROM lang_translations WHERE dict_id=ANY(@names)",
                new { names = chain.Select(t => t.TranslationName).ToArray() }, transaction)).ToLookup(t => t.dict_id);
            var defaultDict = Translations.Default;
            var loading = defaultTranslationProviders.Select(d => d.GetDefaultTranslations()).ToArray();
            Dictionary<string, string?> additionalDefault = new();
            foreach (var defaultProvider in loading)
            {
                foreach (var kv in await defaultProvider)
                {
                    additionalDefault.TryAdd(kv.Key, string.IsNullOrEmpty(kv.Value) ? kv.Key : kv.Value);
                }
            }
            defaultDict = new Translations(additionalDefault, defaultDict);
            var fallback = defaultDict;
            foreach (var dictionary in chain.Skip(1).Reverse())
                fallback = new Translations(all[dictionary.TranslationName].Select(t => KeyValuePair.Create(t.sentence, t.translation)), fallback);
            var translations = new Translations(all[translationName].Select(t => KeyValuePair.Create(t.sentence, t.translation)), fallback);
            return new LoadedTranslations(translations, fallback, translationName, metadata);
        }

        public async Task Save(Translation translation, Translations translations, LanguageMetadata? metadata = null)
        {
            await using (var ctx = contextFactory.CreateContext())
            {
                var conn = ctx.Database.GetDbConnection();
                await conn.OpenAsync();
                await using var transaction = await conn.BeginTransactionAsync();
                await SaveTranslations(conn, transaction, translation.TranslationName, translations);
                if (metadata is not null)
                    await UpdateCustomMetadata(conn, transaction, translation.TranslationName, metadata);
                await transaction.CommitAsync();
            }
            await RefreshTranslations();
        }

        // Returns true for a new installation, false for an update.
        public async Task<bool> SaveLanguagePack(string translationName, Translations translations, string version, LanguageMetadata metadata)
        {
            bool created;
            await using (var ctx = contextFactory.CreateContext())
            {
                var conn = ctx.Database.GetDbConnection();
                await conn.OpenAsync();
                await using var transaction = await conn.BeginTransactionAsync();
                created = await conn.ExecuteAsync(
                    "INSERT INTO lang_dictionaries (dict_id, fallback, source) VALUES (@translationName, @fallback, 'LanguagePack') ON CONFLICT (dict_id) DO NOTHING",
                    new { translationName, fallback = Translations.DefaultLanguage }, transaction) != 0;
                if (await UpdateMetadata(conn, transaction, translationName, version, metadata) == 0)
                    throw new InvalidOperationException($"Translation '{translationName}' is not a downloaded language pack.");
                await SaveTranslations(conn, transaction, translationName, translations);
                await transaction.CommitAsync();
            }
            await RefreshTranslations();
            return created;
        }

        async Task SaveTranslations(DbConnection conn, DbTransaction transaction, string translationName, Translations translations)
        {
            var loadedTranslations = await GetTranslations(translationName, conn, transaction);
            translations = translations.WithFallback(loadedTranslations.Fallback);
            var diffs = loadedTranslations.Translations.CalculateDiff(translations);
            List<string> keys = new List<string>();
            List<string> deletedKeys = new List<string>();
            List<string> values = new List<string>();

            // The basic idea here is that we can remove from
            // the translation any translations which are the same
            // as the fallback. This way, if the fallback gets updated,
            // it will also update the translation.
            foreach (var diff in diffs)
            {
                if (diff is Translations.Diff.Added a)
                {
                    if (a.Value != loadedTranslations.Fallback[a.Key])
                    {
                        keys.Add(a.Key);
                        values.Add(a.Value);
                    }
                }
                else if (diff is Translations.Diff.Modified m)
                {
                    if (m.NewValue != loadedTranslations.Fallback[m.Key])
                    {
                        keys.Add(m.Key);
                        values.Add(m.NewValue);
                    }
                    else
                    {
                        deletedKeys.Add(m.Key);
                    }
                }
                else if (diff is Translations.Diff.Deleted d)
                {
                    deletedKeys.Add(d.Key);
                }
            }
            await conn.ExecuteAsync("INSERT INTO lang_translations SELECT @dict_id, sentence, translation FROM unnest(@keys, @values) AS t(sentence, translation) ON CONFLICT (dict_id, sentence) DO UPDATE SET translation = EXCLUDED.translation; ",
                new
                {
                    dict_id = loadedTranslations.LangName,
                    keys = keys.ToArray(),
                    values = values.ToArray()
                }, transaction);
            await conn.ExecuteAsync("DELETE FROM lang_translations WHERE dict_id=@dict_id AND sentence=ANY(@keys)",
                new
                {
                    dict_id = loadedTranslations.LangName,
                    keys = deletedKeys.ToArray()
                }, transaction);
        }

        public record Translation(string TranslationName, string? Fallback, string Source, JObject Metadata)
        {
            private JObject _metadata = Metadata;
            public LanguageMetadata LanguageMetadata { get; private init; } = Metadata.ToObject<LanguageMetadata>() ?? new();
            public JObject Metadata
            {
                get => _metadata;
                init
                {
                    _metadata = value;
                    LanguageMetadata = value.ToObject<LanguageMetadata>() ?? new();
                }
            }
        }
        /// <summary>
        /// The installed translations as dropdown items, sorted by name. Used by the server-wide language
        /// setting, and shared here so a per-user language setting can list the same languages.
        /// </summary>
        public async Task<List<SelectListItem>> GetTranslationsSelectList()
        {
            return ToSelectListItems(await GetTranslations());
        }

        public static List<SelectListItem> ToSelectListItems(IEnumerable<Translation> translations)
        {
            return translations.Select(t => new SelectListItem(t.TranslationName, t.TranslationName)).OrderBy(t => t.Value).ToList();
        }

        public async Task<Translation[]> GetTranslations()
        {
            await using var ctx = contextFactory.CreateContext();
            return await GetTranslations(ctx.Database.GetDbConnection());
        }

        static async Task<Translation[]> GetTranslations(DbConnection conn, DbTransaction? transaction = null)
        {
            var rows = await conn.QueryAsync<(string dict_id, string? fallback, string? source, string? metadata)>(
                "SELECT * FROM lang_dictionaries", transaction: transaction);
            return rows.Select(r => new Translation(r.dict_id, r.fallback, r.source ?? "", JsonParser.ParseObject(r.metadata ?? "{}"))).ToArray();
        }
        public async Task<Translation?> GetTranslation(string name)
        {
            await using var ctx = contextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            var r = await db.QueryFirstOrDefaultAsync("SELECT * FROM lang_dictionaries WHERE dict_id=@dict_id", new { dict_id = name });
            if (r is null)
                return null;
            return new Translation(r.dict_id, r.fallback, r.source ?? "", JsonParser.ParseObject(r.metadata ?? "{}"));
        }

        public async Task<Translation> CreateTranslation(string langName, string? fallback, string source, LanguageMetadata? metadata = null)
        {
            metadata ??= new();
            var json = JObject.FromObject(metadata);
            await using var ctx = contextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            await db.ExecuteAsync("INSERT INTO lang_dictionaries (dict_id, fallback, source, metadata) VALUES (@langName, @fallback, @source, @metadata::jsonb)",
                new { langName, fallback, source, metadata = json.ToString() });
            await RefreshTranslations();
            return new Translation(langName, fallback, source ?? "", json);
        }

        public async Task DeleteTranslation(string translationName)
        {
            await using var ctx = contextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            await db.ExecuteAsync("DELETE FROM lang_dictionaries WHERE dict_id=@dict_id AND source IN ('Custom', 'LanguagePack')", new { dict_id = translationName });
            await RefreshTranslations();
        }

        public async Task UpdateMetadata(string translationName, string version, LanguageMetadata metadata)
        {
            await using var ctx = contextFactory.CreateContext();
            await UpdateMetadata(ctx.Database.GetDbConnection(), null, translationName, version, metadata);
            await RefreshTranslations();
        }

        static Task<int> UpdateMetadata(DbConnection conn, DbTransaction? transaction, string translationName, string version, LanguageMetadata metadata) =>
            conn.ExecuteAsync(
                "UPDATE lang_dictionaries SET metadata = COALESCE(metadata, '{}'::jsonb) || jsonb_build_object('version', @version::text, 'rtl', @rtl::boolean, 'code', @code::text) WHERE dict_id = @dict_id AND source='LanguagePack'",
                new
                {
                    dict_id = translationName,
                    version,
                    rtl = metadata.Rtl,
                    code = string.IsNullOrWhiteSpace(metadata.Code) ? null : metadata.Code.Trim()
                }, transaction);

        static Task<int> UpdateCustomMetadata(DbConnection conn, DbTransaction? transaction, string translationName, LanguageMetadata metadata) =>
            conn.ExecuteAsync(
                "UPDATE lang_dictionaries SET metadata = COALESCE(metadata, '{}'::jsonb) || @metadata::jsonb WHERE dict_id=@translationName AND source='Custom'",
                new { translationName, metadata = JObject.FromObject(metadata).ToString() }, transaction);
    }
}
