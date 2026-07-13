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

        /// <summary>
        /// The list of filters that shouldn't appear in the search input text
        /// </summary>
        public HashSet<string> UIFilterTypes = new HashSet<string>(["status", "timezone", "exceptionstatus", "unusual", "includearchived", "appid", "startdate", "enddate", "daterange"], StringComparer.OrdinalIgnoreCase);

        public static SearchString Combine(string?[] str)
        => new SearchString(string.Join(",", str.Where(s => !string.IsNullOrWhiteSpace(s))));

        public SearchString(string? str)
        {
            str ??= string.Empty;
            str = str.Trim();
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

        public override string ToString() => ToString(SearchStringFormat.All);

        public string ToString(SearchStringFormat format)
        {
            var filters = Filters
                .Where(kv => format switch
                {
                    SearchStringFormat.All => true,
                    SearchStringFormat.ExceptUIFilters => !UIFilterTypes.Contains(kv.Key),
                    SearchStringFormat.OnlyUIFilters => UIFilterTypes.Contains(kv.Key),
                    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
                })
                .Select(f => string.Join(FilterSeparator, f.Value.Select(v => $"{f.Key}{ValueSeparator}{v}"))).ToList();

            if (format != SearchStringFormat.OnlyUIFilters)
                filters.Add(TextSearch);
            return string.Join(FilterSeparator, filters.Where(x => !string.IsNullOrEmpty(x)));
        }


        [Obsolete("Use ToString(SearchStringFormat.OnlyUIFilters) instead")]
        public string WithoutSearchText() => ToString(SearchStringFormat.OnlyUIFilters);

        [Obsolete("Use SetFilter(key, value, true) instead")]
        public string Toggle(string key, string value)
        {
            var s = Clone();
            s.SetFilter(key, value, true);
            return s.ToString();
        }

        public SearchString Clone() => new(ToString());

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
        public string? GetFilterString(string key)
        {
            key = NormalizeKey(key);
            if (!Filters.TryGetValue(key, out var filter))
                return null;
            return filter.First();
        }

        public (DateTimeOffset? StartDate, DateTimeOffset? EndDate) GetDateRange()
            => GetDateRange(null);
        public (DateTimeOffset? StartDate, DateTimeOffset? EndDate) GetDateRange(TimeZoneInfo? defaultTimeZoneInfo)
        {
            DateTimeOffset? start;
            DateTimeOffset? end;
            if (Filters.TryGetValue("daterange", out var dateRange) && IsValidDateRange(dateRange.FirstOrDefault()))
            {
                start = GetDateRangeDate("startdate", dateRange.First(), defaultTimeZoneInfo);
                end = GetDateRangeDate("enddate", dateRange.First(), defaultTimeZoneInfo);
                return (start, end);
            }
            else
            {
                start = GetFilterDate("startdate", defaultTimeZoneInfo);
                end = GetFilterDate("enddate", defaultTimeZoneInfo);
                return (start, end);
            }
        }

        TimeZoneInfo? GetTimeZoneInfo(TimeZoneInfo? defaultTimeZoneInfo)
        {
            var tz = this.GetExplicitTimeZone();
            if (tz is null || !TimeZones.TryGet(tz, out var tzInfo))
                return defaultTimeZoneInfo;
            return tzInfo;
        }

        public DateTimeOffset? GetFilterDate(string key)
            => GetFilterDate(key, null);
        public DateTimeOffset? GetFilterDate(string key, TimeZoneInfo? defaultTimeZoneInfo)
        {
            key = NormalizeKey(key);
            var tz = GetTimeZoneInfo(defaultTimeZoneInfo);

            if (!Filters.TryGetValue(key, out var filter))
                return null;

            var val = filter.First();
            var dateRangeDate = GetDateRangeDate(key, val, defaultTimeZoneInfo);
            if (dateRangeDate is not null)
                return dateRangeDate;

            // Parsing the date
            if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDateTime))
            {
                return localDateTime.Kind switch
                {
                    DateTimeKind.Local => new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime)).ToUniversalTime(),
                    DateTimeKind.Utc => new DateTimeOffset(localDateTime, TimeSpan.Zero),
                    DateTimeKind.Unspecified when tz is not null => new DateTimeOffset(localDateTime, tz.GetUtcOffset(localDateTime)).ToUniversalTime(),
                    DateTimeKind.Unspecified when tz is null => null,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
            return null;
        }

        private DateTimeOffset? GetDateRangeDate(string key, string val, TimeZoneInfo? defaultTimeZoneInfo)
        {
            var utcNow = DateTimeOffset.UtcNow;
            var rollingStart = val switch
            {
                "-24h" or "-1d" => utcNow.AddDays(-1),
                "-3d" => utcNow.AddDays(-3),
                "-7d" => utcNow.AddDays(-7),
                _ => (DateTimeOffset?)null
            };
            if (rollingStart is not null)
                return rollingStart;
            var tz = GetTimeZoneInfo(defaultTimeZoneInfo);
            if (tz is null)
                return null;
            var now = TimeZoneInfo.ConvertTime(utcNow, tz);
            var today = now.Date;
            var startOfThisWeek = today.AddDays(-(int)today.DayOfWeek);
            var startOfThisMonth = new DateTime(today.Year, today.Month, 1);
            var startOfThisQuarter = new DateTime(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1);
            var startOfThisYear = new DateTime(today.Year, 1, 1);

            var localDate = (key, val) switch
            {
                ("startdate", "today") => today,
                ("startdate", "yesterday") => today.AddDays(-1),
                ("enddate", "yesterday") => today.AddTicks(-1),
                ("startdate", "thisweek") => startOfThisWeek,
                ("startdate", "lastweek") => startOfThisWeek.AddDays(-7),
                ("enddate", "lastweek") => startOfThisWeek.AddTicks(-1),
                ("startdate", "thismonth") => startOfThisMonth,
                ("startdate", "lastmonth") => startOfThisMonth.AddMonths(-1),
                ("enddate", "lastmonth") => startOfThisMonth.AddTicks(-1),
                ("startdate", "last30d") => today.AddDays(-29),
                ("startdate", "thisquarter") => startOfThisQuarter,
                ("startdate", "lastquarter") => startOfThisQuarter.AddMonths(-3),
                ("enddate", "lastquarter") => startOfThisQuarter.AddTicks(-1),
                ("startdate", "thisyear") => startOfThisYear,
                ("startdate", "lastyear") => startOfThisYear.AddYears(-1),
                ("enddate", "lastyear") => startOfThisYear.AddTicks(-1),
                ("startdate", "yeartodate") => startOfThisYear,
                _ => (DateTime?)null
            };

            if (localDate is null)
                return null;
            var local = DateTime.SpecifyKind(localDate.Value, DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz), TimeSpan.Zero);
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
            dateRange is "alltime" or "today" or "yesterday" or "thisweek" or "lastweek"
            or "thismonth" or "lastmonth" or "last30d" or "thisquarter" or "lastquarter" or "thisyear" or "lastyear" or "yeartodate"
            or "-1d" or "-24h" or "-3d" or "-7d";

        public void SetFilter(string filter, string? value = null, bool toggle = false, bool multi = false)
        {
            filter = NormalizeKey(filter);
            if (!toggle)
            {
                if (!multi)
                    Filters.Remove(filter);
                if (value is not null)
                {
                    Filters.Remove(filter, value);
                    Filters.Add(filter, value);
                }
            }
            else
            {
                if (!Filters.ContainsKey(filter) || value is null)
                    SetFilter(filter, value);
                else if (Filters[filter].Contains(value))
                {
                    Filters.Remove(filter, value);
                }
                else
                {
                    if (!multi)
                        Filters.Remove(filter);
                    Filters.Add(filter, value);
                }
            }
        }

        public void SetDateRange(string? dateRange = null, bool toggle = false)
        {
            Filters.Remove("startdate");
            Filters.Remove("enddate");
            SetFilter("daterange", dateRange, toggle);
        }

        public string? GetExplicitTimeZone() => GetFilterString("timezone");
    }
}
