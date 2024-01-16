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

namespace BTCPayServer.Services
{
    public class LocalizerService
    {
        public LocalizerService(ApplicationDbContextFactory contextFactory)
        {
            _ContextFactory = contextFactory;
            _Translations = Translations.Default;
        }
        Translations _Translations;
        public Translations Translations => _Translations;
        private readonly ApplicationDbContextFactory _ContextFactory;

        public async Task Load()
        {
            await using var ctx = _ContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            var all = await conn.QueryAsync<(string key, string value)>("SELECT * FROM translations");
            _Translations = new Translations(all.Select(o => KeyValuePair.Create(o.key, o.value)), Translations.Default);
        }
        public async Task Save(Translations translations)
        {
            translations = new Translations(translations, Translations.Default);
            await using var ctx = _ContextFactory.CreateContext();
            var diffs = Translations.CalculateDiff(translations);
            var conn = ctx.Database.GetDbConnection();
            List<string> keys = new List<string>();
            List<string> deletedKeys = new List<string>();
            List<string> values = new List<string>();
            foreach (var diff in diffs)
            {
                if (diff is Translations.Diff.Added a)
                {
                    if (a.Value != Translations.Default[a.Key])
                    {
                        keys.Add(a.Key);
                        values.Add(a.Value);
                    }
                }
                else if (diff is Translations.Diff.Modified m)
                {
                    if (m.NewValue != Translations.Default[m.Key])
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
            await conn.ExecuteAsync("INSERT INTO translations SELECT key, value FROM unnest(@keys, @values) AS t(key, value) ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value; ",
                new
                {
                    keys = keys.ToArray(),
                    values = values.ToArray()
                });
            await conn.ExecuteAsync("DELETE FROM translations WHERE key=ANY(@keys)",
                new
                {
                    keys = deletedKeys.ToArray()
                });
            _Translations = translations;
        }
    }
}
