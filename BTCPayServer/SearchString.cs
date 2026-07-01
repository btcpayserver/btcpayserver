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
        private static readonly string[] StripFilters = ["status", "exceptionstatus", "unusual", "includearchived", "appid", "startdate", "enddate", "label", "nolabel", "direction"];

        private readonly string _originalString;
        private readonly TimeZoneInfo _timeZone;

        public SearchString(string str, TimeZoneInfo timeZone)
        {
            str ??= string.Empty;
            str = str.Trim();
            _originalString = str;
            _timeZone = timeZone;
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

            var dateFilter = GetFilterDate(key);
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
            List<string> parts = new();
            foreach (var kv in Filters.Where(f => StripFilters.Contains(f.Key)))
            {
                foreach (var value in kv.Value)
                {
                    parts.Add($"{kv.Key}{ValueSeparator}{value}");
                }
            }
            return string.Join(FilterSeparator, parts);
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

        public DateTimeOffset? GetFilterDate(string key)
        {
            key = UnifyKey(key);
            if (!Filters.ContainsKey(key))
                return null;

            var val = Filters[key].First();
            var periodDate = GetPeriodDate(key, val);
            if (periodDate is not null)
                return periodDate;

            switch (val)
            {
                // handle special string values
                case "-24h":
                case "-1d":
                    var lastDay = DateTimeOffset.UtcNow.AddDays(-1);
                    return lastDay - _timeZone.GetUtcOffset(lastDay);
                case "-3d":
                    var lastThreeDays = DateTimeOffset.UtcNow.AddDays(-3);
                    return lastThreeDays - _timeZone.GetUtcOffset(lastThreeDays);
                case "-7d":
                    var lastSevenDays = DateTimeOffset.UtcNow.AddDays(-7);
                    return lastSevenDays - _timeZone.GetUtcOffset(lastSevenDays);
            }

            // default parsing logic
            var success = DateTimeOffset.TryParse(val, null, DateTimeStyles.AssumeUniversal, out var r);
            if (success)
            {
                r = r - _timeZone.GetUtcOffset(r);
                return r;
            }

            return null;
        }

        private DateTimeOffset? GetPeriodDate(string key, string val)
        {
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone);
            var today = now.Date;
            var startOfThisMonth = new DateTime(today.Year, today.Month, 1);
            var startOfThisQuarter = new DateTime(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1);
            var startOfThisYear = new DateTime(today.Year, 1, 1);

            var localDate = (key, val) switch
            {
                ("startdate", "thismonth") => startOfThisMonth,
                ("startdate", "lastmonth") => startOfThisMonth.AddMonths(-1),
                ("enddate", "lastmonth") => startOfThisMonth.AddTicks(-1),
                ("startdate", "last30d") => today.AddDays(-29),
                ("startdate", "thisquarter") => startOfThisQuarter,
                ("startdate", "yeartodate") => startOfThisYear,
                _ => (DateTime?)null
            };

            return localDate is null ? null : ToDateTimeOffset(localDate.Value);
        }

        private DateTimeOffset ToDateTimeOffset(DateTime localDate)
        {
            var local = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _timeZone), TimeSpan.Zero);
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
