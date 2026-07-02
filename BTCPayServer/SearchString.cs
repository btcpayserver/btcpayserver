#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BTCPayServer
{
    public enum SearchStringFormat
    {
        /// <summary>
        /// Include all filters.
        /// </summary>
        All,
        /// <summary>
        /// Exclude filters that are configured via other UI elements (those defined in UIFilters). This is meant to be a
        /// string that we show in an input textbox.
        /// </summary>
        ExceptUIFilters,
        /// <summary>
        /// Only include filters that are configured via other UI elements (those defined in UIFilters).
        /// </summary>
        OnlyUIFilters
    }

    public class SearchString
    {
        private const char FilterSeparator = ',';
        private const char ValueSeparator = ':';
        private static readonly string[] UIFilters = ["status", "exceptionstatus", "unusual", "includearchived", "appid", "startdate", "enddate", "label", "nolabel", "direction"];

        private readonly TimeZoneInfo _timeZone;

        public static SearchString Combine(string?[] str, TimeZoneInfo timeZone)
        => new SearchString(string.Join(",", str.Where(s => !string.IsNullOrWhiteSpace(s))), timeZone);

        public SearchString(string? str, TimeZoneInfo timeZone)
        {
            str ??= string.Empty;
            str = str.Trim();
            _timeZone = timeZone;
            var splitted = str.Split(new [] { FilterSeparator }, StringSplitOptions.RemoveEmptyEntries);
            Filters
                = splitted
                    .Select(t => t.Split(new [] { ValueSeparator }, 2, StringSplitOptions.RemoveEmptyEntries))
                    .Where(kv => kv.Length == 2)
                    .Select(kv => new KeyValuePair<string, string>(NormalizeKey(kv[0]), kv[1]))
                    .ToMultiValueDictionary(o => o.Key, o => o.Value);
            TextSearch = splitted.FirstOrDefault(a => a.IndexOf(ValueSeparator, StringComparison.OrdinalIgnoreCase) == -1)?.Trim() ?? "";
        }

        /// <summary>
        /// The part of the search string that is free form text (not a filter)
        /// </summary>
        public string TextSearch { get; set; }

        /// <summary>
        /// The search string we should show in an input textbox.
        /// Some filters are excluded from the string as they are configured via other UI elements
        /// </summary>
        [Obsolete("Use ToString(SearchStringFormat.OnlyUIFilters) instead")]
        public string TextCombined => ToString(SearchStringFormat.OnlyUIFilters);

        public MultiValueDictionary<string, string> Filters { get; }
        public bool IsEmpty => string.IsNullOrWhiteSpace(TextSearch) && !Filters.Any();

        public override string ToString() => ToString(SearchStringFormat.All);

        public string ToString(SearchStringFormat format)
        {
            var filters = Filters
                .Where(kv => format switch
                {
                    SearchStringFormat.All => true,
                    SearchStringFormat.ExceptUIFilters => !UIFilters.Contains(kv.Key),
                    SearchStringFormat.OnlyUIFilters => UIFilters.Contains(kv.Key),
                    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
                })
                .Select(f => string.Join(FilterSeparator, f.Value.Select(v => $"{f.Key}{ValueSeparator}{v}"))).ToList();

            if (format != SearchStringFormat.OnlyUIFilters)
                filters.Add(TextSearch);
            return string.Join(FilterSeparator, filters.Where(x => !string.IsNullOrEmpty(x)));
        }


        [Obsolete("Use ToString(SearchStringFormat.OnlyUIFilters) instead")]
        public string WithoutSearchText() => ToString(SearchStringFormat.OnlyUIFilters);

        public string Toggle(string key, string value)
        {
            key = NormalizeKey(key);
            var clone = Clone();
            if (clone.ContainsFilter(key))
                clone.Filters.Remove(key);
            else
                clone.Filters.Add(key, value);
            return clone.ToString();
        }

        public SearchString Clone() => new SearchString(ToString(), _timeZone);

        public string[]? GetFilterArray(string key)
        {
            key = NormalizeKey(key);
            return Filters.TryGetValue(key, out var filter) ? filter.ToArray() : null;
        }

        public bool? GetFilterBool(string key)
        {
            key = NormalizeKey(key);
            if (!Filters.TryGetValue(key, out var filter))
                return null;

            return bool.TryParse(filter.First(), out var r) ? r : null;
        }

        public DateTimeOffset? GetFilterDate(string key)
        {
            key = NormalizeKey(key);
            if (!Filters.TryGetValue(key, out var filter))
                return null;

            var val = filter.First();
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

            if (localDate is null)
                return null;
            var local = DateTime.SpecifyKind(localDate.Value, DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _timeZone), TimeSpan.Zero);
        }

        public bool ContainsFilter(string key) => Filters.ContainsKey(NormalizeKey(key));
        public int CountArrayFilter(string type) =>
            ContainsFilter(type) ? GetFilterArray(type)!.Length : 0;

        public bool HasArrayFilter(string type, string? key = null) =>
            ContainsFilter(type) && (key is null || GetFilterArray(type).Contains(key));

        public bool HasBooleanFilter(string key) =>
            ContainsFilter(key) && GetFilterBool(key) is true;

        private string NormalizeKey(string key) => key.ToLowerInvariant().Trim();

        public void SetFilter(string filter, string? value = null)
        {
            Filters.Remove(filter);
            if (value is not null)
                Filters.Add(filter, value);
        }
    }
}
