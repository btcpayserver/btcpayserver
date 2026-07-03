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
        public HashSet<string> UIFilters = new HashSet<string>(["status", "exceptionstatus", "unusual", "includearchived", "appid", "startdate", "enddate", "daterange"], StringComparer.OrdinalIgnoreCase);

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
            var s = Clone();
            s.SetFilter(key, value, true);
            return s.ToString();
        }

        public SearchString Clone() => new(ToString(), _timeZone);

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

        public (DateTimeOffset? StartData, DateTimeOffset? EndDate) GetPeriod()
        {
            DateTimeOffset? start = null;
            DateTimeOffset? end = null;
            if (Filters.TryGetValue("daterange", out var dateRange) && IsValidDateRange(dateRange.FirstOrDefault()))
            {
                start = GetDateRangeDate("startdate", dateRange.First());
                end = GetDateRangeDate("enddate", dateRange.First());
                return (start, end);
            }
            else
            {
                start = GetFilterDate("startdate");
                if (start != null)
                    end = GetFilterDate("enddate");
                return (start, end);
            }
        }

        public DateTimeOffset? GetFilterDate(string key)
        {
            key = NormalizeKey(key);

            if (!Filters.TryGetValue(key, out var filter))
                return null;

            var val = filter.First();
            var dateRangeDate = GetDateRangeDate("startdate", val);
            if (dateRangeDate is not null)
                return dateRangeDate;

            // Parsing the date
            if (DateTime.TryParse(val, null, DateTimeStyles.None, out var localDateTime))
            {
                localDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);

                var offset = _timeZone.GetUtcOffset(localDateTime);
                var localDateTimeOffset = new DateTimeOffset(localDateTime, offset);

                return localDateTimeOffset.ToUniversalTime();
            }

            return null;
        }

        private DateTimeOffset? GetDateRangeDate(string key, string val)
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
                (_, "-24h" or "-1d") => today.AddDays(-1),
                (_, "-3d") => today.AddDays(-3),
                (_, "-7d") => today.AddDays(-7),
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


        public static bool IsValidDateRange(string? dateRange) =>
            dateRange is "alltime" or "thismonth" or "lastmonth" or "last30d" or "thisquarter" or "yeartodate";

        public void SetFilter(string filter, string? value = null, bool toggle = false)
        {
            filter = NormalizeKey(filter);
            if (!toggle)
            {
                Filters.Remove(filter);
                if (value is not null)
                    Filters.Add(filter, value);
            }
            else
            {
                if (!Filters.ContainsKey(filter) || value is null)
                    SetFilter(filter, value);
                else if (Filters[filter].First() == value)
                {
                    SetFilter(filter);
                }
                else
                {
                    SetFilter(filter, value);
                }
            }
        }

        public void SetDateRange(string? dateRange = null, bool toggle = false)
        {
            Filters.Remove("startdate");
            Filters.Remove("enddate");
            SetFilter("daterange", dateRange, toggle);
        }
    }
}
