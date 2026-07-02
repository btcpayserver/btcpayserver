#nullable enable
using System;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.DateOptionsToggle;

public class DateOptionsToggle : ViewComponent
{
    public IViewComponentResult Invoke(
        SearchString search,
        string? customRangeTitle = null)
    => View(new DateOptionsToggleModel
    {
        Search = search ?? throw new ArgumentNullException(nameof(search)),
        CustomRangeTitle = customRangeTitle ?? "Filter by Custom Range",
        Url = Url
    });
}

public class DateOptionsToggleModel
{
    private const string SearchTermRouteKey = "searchTerm";

    public required SearchString Search { get; init; }
    public required string CustomRangeTitle { get; init; }
    public required IUrlHelper Url { get; init; }

    public bool HasDateFilter => Search.HasArrayFilter("startdate") || Search.HasArrayFilter("enddate") || Search.HasArrayFilter("period");

    public bool HasCustomDateFilter =>
        HasDateFilter &&
        (IsDate("startdate") && (!Search.HasArrayFilter("enddate") || IsDate("enddate")));

    private bool IsDate(string val) => DateTimeOffset.TryParse(val, null, DateTimeStyles.AssumeUniversal, out var r);

    public bool HasPeriod(string value) => Search.HasArrayFilter("period", value);

    public static string RemoveDatePreset(string? search)
    {
        var s = new SearchString(search, TimeZoneInfo.Utc);
        s.Filters.Remove("startdate");
        s.Filters.Remove("enddate");
        return s.ToString();
    }
}
