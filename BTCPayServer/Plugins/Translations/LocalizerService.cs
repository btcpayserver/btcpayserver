#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Microsoft.Extensions.Logging;
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
        IEnumerable<IDefaultTranslationProvider> defaultTranslationProviders)
    {
        public record LoadedTranslations(Translations Translations, Translations Fallback, string LangName, bool Rtl);
        LoadedTranslations _LoadedTranslations = new(Translations.Default, Translations.Default, Translations.DefaultLanguage, false);

        readonly AsyncLocal<LoadedTranslations?> _requestTranslations = new();
        readonly ConcurrentDictionary<string, Task<LoadedTranslations?>> _userTranslations = new(StringComparer.Ordinal);
        LoadedTranslations Current => _requestTranslations.Value ?? _LoadedTranslations;
        public Translations Translations => Current.Translations;

        // Whether the language used for the current request is written right-to-left.
        public bool IsRtl => Current.Rtl;


        public string ServerLanguage => _LoadedTranslations.LangName;
        readonly ConcurrentDictionary<string, string?> _userChoices = new();

        public void SetUserLanguage(string userId, string? translationName) => _userChoices[userId] = translationName;

        /// <summary>
        /// Load the translation of the server into memory
        /// </summary>
        /// <returns></returns>
        public async Task Load()
        {
            try
            {
                _LoadedTranslations = await GetTranslations(settingsAccessor.Settings.LangTranslation);
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

            var loading = _userTranslations.GetOrAdd(translationName, LoadIfInstalled);
            try
            {
                return await loading;
            }
            catch
            {
                _userTranslations.TryRemove(KeyValuePair.Create(translationName, loading));
                throw;
            }
        }

        public async Task<LoadedTranslations?> GetTranslationsForUser(ClaimsPrincipal principal, UserManager<ApplicationUser> userManager)
        {
            try
            {
                var userId = userManager.GetUserId(principal);
                if (userId is null)
                    return null;
                if (!_userChoices.TryGetValue(userId, out var choice))
                {
                    var stored = (await userManager.FindByIdAsync(userId))?.GetBlob()?.LangTranslation;
                    choice = _userChoices.GetOrAdd(userId, stored);
                }

                return await GetUserTranslations(choice);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve the user's language, using the server language");
                return null;
            }
        }
        public void SetRequestTranslations(LoadedTranslations? translations)
        {
            _requestTranslations.Value = translations;
        }

        void InvalidateUserTranslations() => _userTranslations.Clear();

        async Task<LoadedTranslations?> LoadIfInstalled(string translationName)
        {
            return await GetTranslation(translationName) is null ? null : await GetTranslations(translationName);
        }

        public async Task<LoadedTranslations> GetTranslations(string translationName)
        {
            await using var ctx = contextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            var all = await conn.QueryAsync<(bool fallback, string sentence, string? translation)>(
                "SELECT 'f'::BOOL fallback, sentence, translation FROM translations WHERE dict_id=@dict_id " +
                "UNION ALL " +
                "SELECT 't'::BOOL fallback, sentence, translation FROM translations WHERE dict_id=(SELECT fallback FROM lang_dictionaries WHERE dict_id=@dict_id)",
            new
            {
                dict_id = translationName,
            });
            var metadata = await conn.QueryFirstOrDefaultAsync<string?>("SELECT metadata FROM lang_dictionaries WHERE dict_id=@dict_id", new { dict_id = translationName });
            var rtl = metadata is not null && JObject.Parse(metadata)["rtl"]?.Value<bool>() == true;
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
            var fallback = new Translations(all.Where(a => a.fallback).Select(o => KeyValuePair.Create(o.sentence, o.translation)), defaultDict);
            var translations = new Translations(all.Where(a => !a.fallback).Select(o => KeyValuePair.Create(o.sentence, o.translation)), fallback);
            return new LoadedTranslations(translations, fallback, translationName, rtl);
        }

        public async Task Save(Translation translation, Translations translations)
        {
            var loadedTranslations = await GetTranslations(translation.TranslationName);
            translations = translations.WithFallback(loadedTranslations.Fallback);
            await using var ctx = contextFactory.CreateContext();
            var diffs = loadedTranslations.Translations.CalculateDiff(translations);
            var conn = ctx.Database.GetDbConnection();
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
                });
            await conn.ExecuteAsync("DELETE FROM lang_translations WHERE dict_id=@dict_id AND sentence=ANY(@keys)",
                new
                {
                    dict_id = loadedTranslations.LangName,
                    keys = deletedKeys.ToArray()
                });

            if (_LoadedTranslations.LangName == loadedTranslations.LangName)
                _LoadedTranslations = loadedTranslations with { Translations = translations };
            InvalidateUserTranslations();
        }

        public record Translation(string TranslationName, string? Fallback, string Source, JObject Metadata);
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
            var db = ctx.Database.GetDbConnection();
            var rows = await db.QueryAsync<(string dict_id, string? fallback, string? source, string? metadata)>("SELECT * FROM lang_dictionaries");
            return rows.Select(r => new Translation(r.dict_id, r.fallback, r.source ?? "", JObject.Parse(r.metadata ?? "{}"))).ToArray();
        }
        public async Task<Translation?> GetTranslation(string name)
        {
            await using var ctx = contextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            var r = await db.QueryFirstOrDefaultAsync("SELECT * FROM lang_dictionaries WHERE dict_id=@dict_id", new { dict_id = name });
            if (r is null)
                return null;
            return new Translation(r.dict_id, r.fallback, r.source ?? "", JObject.Parse(r.metadata ?? "{}"));
        }

        public async Task<Translation> CreateTranslation(string langName, string? fallback, string source)
        {
            await using var ctx = contextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            await db.ExecuteAsync("INSERT INTO lang_dictionaries (dict_id, fallback, source) VALUES (@langName, @fallback, @source)", new { langName, fallback, source });
            InvalidateUserTranslations();
            return new Translation(langName, fallback, source ?? "", new JObject());
        }

        public async Task DeleteTranslation(string translationName)
        {
            await using var ctx = contextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            await db.ExecuteAsync("DELETE FROM lang_dictionaries WHERE dict_id=@dict_id AND source IN ('Custom', 'LanguagePack')", new { dict_id = translationName });
            InvalidateUserTranslations();
        }

        public async Task UpdateMetadata(string translationName, string version, bool rtl)
        {
            await using var ctx = contextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            await db.ExecuteAsync(
                "UPDATE lang_dictionaries SET metadata = COALESCE(metadata, '{}'::jsonb) || jsonb_build_object('version', @version::text, 'rtl', @rtl::boolean) WHERE dict_id = @dict_id",
                new { dict_id = translationName, version, rtl });
            InvalidateUserTranslations();
        }
    }
}
