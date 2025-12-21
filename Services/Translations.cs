#nullable enable
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Newtonsoft.Json.Linq;

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
        public static bool TryCreateFromJson(string text, [MaybeNullWhen(false)] out Translations translations)
        {
            translations = null;
            try
            {
                translations = CreateFromJson(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static Translations CreateFromJson(string text)
        {
            text = (text ?? "{}");
            var translations = new List<(string key, string? value)>();
            foreach (var prop in JObject.Parse(text).Properties())
            {
                var v = prop.Value.Value<string>();
                if (string.IsNullOrEmpty(v))
                    translations.Add((prop.Name, prop.Name));
                else
                    translations.Add((prop.Name, v));
            }
            return new Translations(translations
                                    .Select(t => KeyValuePair.Create(t.key, t.value)));
        }

        public Translations(IEnumerable<KeyValuePair<string, string?>> records) : this (records, null)
        {
        }
        public Translations(IEnumerable<KeyValuePair<string, string?>> records, Translations? fallback)
        {
            Dictionary<string, string> thisRecords = new Dictionary<string, string>();
            foreach (var r in records)
            {
                var v = r.Value?.Trim();
                if (string.IsNullOrEmpty(v))
                    continue;
                thisRecords.TryAdd(r.Key.Trim(), v);
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

        public Translations WithFallback(Translations? fallback)
        {
            return new Translations(this!, fallback);
        }
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return Records.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        public string ToJsonFormat()
        {
            JObject obj = new JObject();
            foreach (var record in Records)
            {
                obj.Add(record.Key, record.Value);
            }
            return obj.ToString(Newtonsoft.Json.Formatting.Indented);
        }
    }
}
