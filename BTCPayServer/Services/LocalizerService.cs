#nullable enable
using System.Collections;
using System.Collections.Frozen;
using Dapper;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using static BTCPayServer.Services.LocalizerService;

namespace BTCPayServer.Services
{
    public interface IDefaultTranslationProvider
    {
        Task<KeyValuePair<string, string?>[]> GetDefaultTranslations();
    }
    public class InMemoryDefaultTranslationProvider : IDefaultTranslationProvider
    {
        private readonly KeyValuePair<string, string?>[] _values;

        public InMemoryDefaultTranslationProvider(KeyValuePair<string, string?>[] values)
        {
            _values = values;
        }
        public Task<KeyValuePair<string, string?>[]> GetDefaultTranslations()
        {
            return Task.FromResult(_values);
        }
    }
    public class LocalizerService
    {
        public LocalizerService(
            ILogger<LocalizerService> logger,
            ApplicationDbContextFactory contextFactory,
            ISettingsAccessor<PoliciesSettings> settingsAccessor,
            IEnumerable<IDefaultTranslationProvider> defaultTranslationProviders)
        {
            _logger = logger;
            _ContextFactory = contextFactory;
            _settingsAccessor = settingsAccessor;
            _defaultTranslationProviders = defaultTranslationProviders;
            _LoadedTranslations = new LoadedTranslations(Translations.Default, Translations.Default, Translations.DefaultLanguage);
        }

        public record LoadedTranslations(Translations Translations, Translations Fallback, string LangName);
        LoadedTranslations _LoadedTranslations;
        public Translations Translations => _LoadedTranslations.Translations;

        private readonly ILogger<LocalizerService> _logger;
        private readonly ApplicationDbContextFactory _ContextFactory;
        private readonly ISettingsAccessor<PoliciesSettings> _settingsAccessor;
        private readonly IEnumerable<IDefaultTranslationProvider> _defaultTranslationProviders;

        /// <summary>
        /// Load the translation of the server into memory
        /// </summary>
        /// <returns></returns>
        public async Task Load()
        {
            try
            {
                _LoadedTranslations = await GetTranslations(_settingsAccessor.Settings.LangDictionary);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load translations");
                throw;
            }
        }

        public async Task<LoadedTranslations> GetTranslations(string dictionaryName)
        {
            var ctx = _ContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            var all = await conn.QueryAsync<(bool fallback, string sentence, string? translation)>(
                "SELECT 'f'::BOOL fallback, sentence, translation FROM translations WHERE dict_id=@dict_id " +
                "UNION ALL " +
                "SELECT 't'::BOOL fallback, sentence, translation FROM translations WHERE dict_id=(SELECT fallback FROM lang_dictionaries WHERE dict_id=@dict_id)",
            new
            {
                dict_id = dictionaryName,
            });
            var defaultDict = Translations.Default;
            var loading = _defaultTranslationProviders.Select(d => d.GetDefaultTranslations()).ToArray();
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
            return new LoadedTranslations(translations, fallback, dictionaryName);
        }

        public async Task Save(Dictionary dictionary, Translations translations)
        {
            var loadedTranslations = await GetTranslations(dictionary.DictionaryName);
            translations = translations.WithFallback(loadedTranslations.Fallback);
            await using var ctx = _ContextFactory.CreateContext();
            var diffs = loadedTranslations.Translations.CalculateDiff(translations);
            var conn = ctx.Database.GetDbConnection();
            List<string> keys = new List<string>();
            List<string> deletedKeys = new List<string>();
            List<string> values = new List<string>();

            // The basic idea here is that we can remove from
            // the dictionary any translations which are the same
            // as the fallback. This way, if the fallback get updated,
            // it will also update the dictionary.
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
        }

        public record Dictionary(string DictionaryName, string? Fallback, string Source, JObject Metadata);
        public async Task<Dictionary[]> GetDictionaries()
        {
            await using var ctx = _ContextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            var rows = await db.QueryAsync<(string dict_id, string? fallback, string? source, string? metadata)>("SELECT * FROM lang_dictionaries");
            return rows.Select(r => new Dictionary(r.dict_id, r.fallback, r.source ?? "", JObject.Parse(r.metadata ?? "{}"))).ToArray();
        }
        public async Task<Dictionary?> GetDictionary(string name)
        {
            await using var ctx = _ContextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            var r = await db.QueryFirstAsync("SELECT * FROM lang_dictionaries WHERE dict_id=@dict_id", new { dict_id = name });
            if (r is null)
                return null;
            return new Dictionary(r.dict_id, r.fallback, r.source ?? "", JObject.Parse(r.metadata ?? "{}"));
        }

        public async Task<Dictionary> CreateDictionary(string langName, string? fallback, string source)
        {
            await using var ctx = _ContextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            await db.ExecuteAsync("INSERT INTO lang_dictionaries (dict_id, fallback, source) VALUES (@langName, @fallback, @source)", new { langName, fallback, source });
            return new Dictionary(langName, fallback, source ?? "", new JObject());
        }

        public async Task DeleteDictionary(string dictionary)
        {
            await using var ctx = _ContextFactory.CreateContext();
            var db = ctx.Database.GetDbConnection();
            await db.ExecuteAsync("DELETE FROM lang_dictionaries WHERE dict_id=@dict_id AND source='Custom'", new { dict_id = dictionary });
        }
    }
}
