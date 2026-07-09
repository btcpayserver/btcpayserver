#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.DateRangeSelector;

public class DateRangeSelector : ViewComponent
{
    public IViewComponentResult Invoke(
        SearchString search,
        string? customRangeTitle = null)
    => View(new DateRangeSelectorModel
    {
        Search = search ?? throw new ArgumentNullException(nameof(search)),
        CustomRangeTitle = customRangeTitle ?? "Filter by Custom Range",
    });
}

public class DateRangeSelectorModel
{
    private const string SearchTermRouteKey = "searchTerm";
    private const string InputDateFormat = "yyyy-MM-ddTHH:mm:ss";
    private (DateTimeOffset? StartDate, DateTimeOffset? EndDate)? _dateRange;

    public required SearchString Search { get; init; }
    public required string CustomRangeTitle { get; init; }


    public DateRangeSelectorModel()
    {
        var tz = TimeZoneInfo.GetSystemTimeZones()
            .Select(t => (Id: t.Id, Name: t.DisplayName))
            .ToList();
        tz.Insert(0, default);
        TimeZones = tz.ToArray();
    }

    public (string Id, string Name)[] TimeZones { get; }

    public bool HasDateFilter => Search.HasArrayFilter("startdate") || Search.HasArrayFilter("enddate") || Search.HasArrayFilter("daterange");

    public bool HasCustomDateFilter =>
        HasDateFilter &&
        (IsDate("startdate") && (!Search.HasArrayFilter("enddate") || IsDate("enddate")));

    private bool IsDate(string val) => Search.GetFilterDate(val, TimeZoneInfo.Utc) is not null;

    public bool HasDateRange(string value) => Search.HasArrayFilter("daterange", value);

    public (DateTimeOffset? StartDate, DateTimeOffset? EndDate) DateRange =>
        _dateRange ??= Search.GetDateRange(TimeZoneInfo.Utc);

    public string? StartDateInputValue => FormatInputDate(LocalDate(DateRange.StartDate));
    public string? EndDateInputValue => FormatInputDate(LocalDate(DateRange.EndDate));

    private DateTimeOffset? LocalDate(DateTimeOffset? d)
    {
        if (d is null)
            return null;
        if (BTCPayServer.TimeZones.TryGet(Search.GetExplicitTimeZone() ?? "") is {} tz)
            d = TimeZoneInfo.ConvertTime(d.Value, tz);
        return d;
    }

    private static string? FormatInputDate(DateTimeOffset? date) =>
        date?.ToString(InputDateFormat, CultureInfo.InvariantCulture);

    public static string RemoveDatePreset(string? search)
    {
        var s = new SearchString(search);
        s.Filters.Remove("daterange");
        s.Filters.Remove("startdate");
        s.Filters.Remove("enddate");
        return s.ToString();
    }
}
