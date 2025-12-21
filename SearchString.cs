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
        private static readonly string[] StripFilters = ["status", "exceptionstatus", "unusual", "includearchived", "appid", "startdate", "enddate"];

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
            // combine raw search term and filters which don't have a special UI (e.g. orderid)
            var textFilters = Filters
                .Where(f => !StripFilters.Contains(f.Key))
                .Select(f => string.Join(FilterSeparator, f.Value.Select(v => $"{f.Key}{ValueSeparator}{v}"))).ToList();
            TextFilters = textFilters.Any() ? string.Join(FilterSeparator, textFilters) : null;
            TextSearch = splitted.FirstOrDefault(a => a.IndexOf(ValueSeparator, StringComparison.OrdinalIgnoreCase) == -1)?.Trim();
        }

        public string TextSearch { get; private set; }
        public string TextFilters { get; private set; }

        public string TextCombined => string.Join(FilterSeparator, new []{ TextFilters, TextSearch }.Where(x => !string.IsNullOrEmpty(x)));

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
            var txt = ToString();
            if (!string.IsNullOrEmpty(TextSearch)) txt = Finalize(txt.Replace(TextSearch, string.Empty));
            if (!string.IsNullOrEmpty(TextFilters)) txt = Finalize(txt.Replace(TextFilters, string.Empty));
            return Finalize(txt).Trim();
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
            var value = str.Trim().TrimStart(FilterSeparator).TrimEnd(FilterSeparator);
            return string.IsNullOrEmpty(value) ? " " : value;
        }
    }
}
