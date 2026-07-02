#nullable enable
using System;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.DateOptionsToggle;

public class DateOptionsToggle : ViewComponent
{
    public IViewComponentResult Invoke(
        SearchString? search,
        string? customRangeTitle = null)
    => View(new DateOptionsToggleModel
    {
        Search = search,
        CustomRangeTitle = customRangeTitle ?? "Filter by Custom Range",
        Url = Url
    });
}

public class DateOptionsToggleModel
{
    private const string SearchTermRouteKey = "searchTerm";

    public SearchString? Search { get; init; }
    public required string CustomRangeTitle { get; init; }
    public required IUrlHelper Url { get; init; }

    public bool HasDateFilter => HasArrayFilter("startdate") || HasArrayFilter("enddate");

    public bool HasCustomDateFilter =>
        HasDateFilter &&
        !HasDatePreset("thismonth") &&
        !HasDatePreset("lastmonth") &&
        !HasDatePreset("last30d") &&
        !HasDatePreset("thisquarter") &&
        !HasDatePreset("yeartodate");

    public bool HasDatePreset(string value) =>
        HasArrayFilter("startdate", value) && (value != "lastmonth" || HasArrayFilter("enddate", value));

    public bool HasArrayFilter(string type, string? key = null) =>
        Search?.ContainsFilter(type) is true && (key is null || Search.GetFilterArray(type).Contains(key));

    public static string RemoveDatePreset(string? search)
    {
        var s = new SearchString(search, TimeZoneInfo.Utc);
        s.RunFilterCommand("alltime");
        return s.ToString();
    }
}
