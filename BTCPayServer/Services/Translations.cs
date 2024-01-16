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
using System.Runtime.InteropServices;

namespace BTCPayServer.Services
{
    public partial class Translations : IEnumerable<KeyValuePair<string, string>>
    {
        public record Diff(string Key)
        {
            public record Deleted(string Key, string OldValue) : Diff(Key);
            public record Added(string Key, string Value) : Diff(Key);
            public record Modified(string Key, string NewValue, string OldValue) : Diff(Key);
        }
        public static Translations CreateFromText(string text)
        {
            text = (text ?? "").Replace("\r\n", "\n");
            List<(string key, string value)> translations = new List<(string key, string value)>();
            foreach (var line in text.Split("\n", StringSplitOptions.RemoveEmptyEntries))
            {
                var splitted = line.Split("=>", StringSplitOptions.RemoveEmptyEntries);
                if (splitted is [var key, var value])
                {
                    translations.Add((key, value));
                }
                else if (splitted is [var key2])
                {
                    translations.Add((key2, key2));
                }
            }
            return new Translations(translations
                                    .Select(t => KeyValuePair.Create(t.key, t.value)));
        }

        public Translations(IEnumerable<KeyValuePair<string, string>> records) : this (records, null)
        {
        }
        public Translations(IEnumerable<KeyValuePair<string, string>> records, Translations? fallback)
        {
            Dictionary<string, string> thisRecords = new Dictionary<string, string>();
            foreach (var r in records)
            {
                thisRecords.TryAdd(r.Key.Trim(), r.Value.Trim());
            }
            if (fallback is not null)
            {
                foreach (var r in fallback.Records)
                {
                    thisRecords.TryAdd(r.Key, r.Value);
                }
            }
            Records = thisRecords.ToFrozenDictionary();
        }
        public readonly FrozenDictionary<string, string> Records;

        public string? this[string? key] => key is null ? null : Records.TryGetValue(key, out var v) ? v : null;

        public Diff[] CalculateDiff(Translations translations)
        {
            List<Diff> diff = new List<Diff>(translations.Records.Count + 10);
            foreach (var kv in translations)
            {
                if (Records.TryGetValue(kv.Key, out var oldValue))
                {
                    if (oldValue != kv.Value)
                        diff.Add(new Diff.Modified(kv.Key, kv.Value, oldValue));
                }
                else
                {
                    diff.Add(new Diff.Added(kv.Key, kv.Value));
                }
            }
            foreach (var kv in this)
            {
                if (!translations.Records.ContainsKey(kv.Key))
                    diff.Add(new Diff.Deleted(kv.Key, kv.Value));
            }
            return diff.ToArray();
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return Records.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public string ToTextFormat()
        {
            return string.Join('\n', Records.OrderBy(r => r.Key).Select(r => $"{r.Key} => {r.Value}").ToArray());
        }
    }
}
