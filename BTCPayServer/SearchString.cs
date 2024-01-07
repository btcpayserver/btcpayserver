using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BTCPayServer
{
    public class SearchString
    {
        private const char FilterSeparator = ',';
        private const char ValueSeparator = ':';
        
        private readonly string _originalString;
        private readonly int _timezoneOffset;

        public SearchString(string str, int timezoneOffset = 0)
        {
            str ??= string.Empty;
            str = str.Trim();
            _originalString = str;
            _timezoneOffset = timezoneOffset;
            TextSearch = _originalString;
            var splitted = str.Split(new [] { FilterSeparator }, StringSplitOptions.RemoveEmptyEntries);
            Filters
                = splitted
                    .Select(t => t.Split(new [] { ValueSeparator }, 2, StringSplitOptions.RemoveEmptyEntries))
                    .Where(kv => kv.Length == 2)
                    .Select(kv => new KeyValuePair<string, string>(UnifyKey(kv[0]), kv[1]))
                    .ToMultiValueDictionary(o => o.Key, o => o.Value);

            var val = splitted.FirstOrDefault(a => a.IndexOf(ValueSeparator, StringComparison.OrdinalIgnoreCase) == -1);
            TextSearch = val != null ? val.Trim() : string.Empty;
        }

        public string TextSearch { get; private set; }

        public MultiValueDictionary<string, string> Filters { get; }

        public override string ToString()
        {
            return _originalString;
        }

        public string Toggle(string key, string value)
        {
            key = UnifyKey(key);
            var keyValue = $"{key}{ValueSeparator}{value}";
            var prependOnInsert = string.IsNullOrEmpty(ToString()) ? string.Empty : $"{ToString()}{FilterSeparator}";
            if (!ContainsFilter(key)) return Finalize($"{prependOnInsert}{keyValue}");

            var boolFilter = GetFilterBool(key);
            if (boolFilter != null)
            {
                return Finalize(ToString().Replace(keyValue, string.Empty));
            }

            var dateFilter = GetFilterDate(key, _timezoneOffset);
            if (dateFilter != null)
            {
                var current = GetFilterArray(key).First();
                var oldValue = $"{key}{ValueSeparator}{current}";
                var newValue = string.IsNullOrEmpty(value) || current == value ? string.Empty : keyValue;
                return Finalize(_originalString.Replace(oldValue, newValue));
            }
            
            var arrayFilter = GetFilterArray(key);
            if (arrayFilter != null)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return Finalize(arrayFilter.Aggregate(ToString(), (current, filter) => 
                        current.Replace($"{key}{ValueSeparator}{filter}", string.Empty)));
                }
                return Finalize(arrayFilter.Contains(value)
                    ? ToString().Replace(keyValue, string.Empty)
                    : $"{prependOnInsert}{keyValue}"
                    );
            }

            return Finalize(ToString());
        }

        public string WithoutSearchText()
        {
            return string.IsNullOrEmpty(TextSearch)
                ? Finalize(ToString())
                : Finalize(ToString()).Replace(TextSearch, string.Empty);
        }

        public string[] GetFilterArray(string key)
        {
            key = UnifyKey(key);
            return Filters.ContainsKey(key) ? Filters[key].ToArray() : null;
        }

        public bool? GetFilterBool(string key)
        {
            key = UnifyKey(key);
            if (!Filters.ContainsKey(key))
                return null;

            return bool.TryParse(Filters[key].First(), out var r) ? r : null;
        }

        public DateTimeOffset? GetFilterDate(string key, int timezoneOffset)
        {
            key = UnifyKey(key);
            if (!Filters.ContainsKey(key))
                return null;

            var val = Filters[key].First();
            switch (val)
            {
                // handle special string values
                case "-24h":
                case "-1d":
                    return DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(timezoneOffset);
                case "-3d":
                    return DateTimeOffset.UtcNow.AddDays(-3).AddMinutes(timezoneOffset);
                case "-7d":
                    return DateTimeOffset.UtcNow.AddDays(-7).AddMinutes(timezoneOffset);
            }

            // default parsing logic
            var success = DateTimeOffset.TryParse(val, null, DateTimeStyles.AssumeUniversal, out var r);
            if (success)
            {
                r = r.AddMinutes(timezoneOffset);
                return r;
            }

            return null;
        }

        public bool ContainsFilter(string key)
        {
           return Filters.ContainsKey(UnifyKey(key));
        }

        private string UnifyKey(string key)
        {
            return key.ToLowerInvariant().Trim();
        }
        
        private static string Finalize(string str)
        {
            var value = str.TrimStart(FilterSeparator).TrimEnd(FilterSeparator);
            return string.IsNullOrEmpty(value) ? " " : value;
        }
    }
}
