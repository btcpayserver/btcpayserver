#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Components.DateOptionsToggle;

public class DateOptionsToggle : ViewComponent
{
    public IViewComponentResult Invoke(
        SearchString? search,
        string action,
        string? controller = null,
        IDictionary<string, string>? routeValues = null,
        string? customRangeTitle = null,
        bool showAllTime = false)
    {
        return View(new DateOptionsToggleModel
        {
            Search = search,
            Action = action,
            Controller = controller,
            RouteValues = routeValues ?? new Dictionary<string, string>(),
            CustomRangeTitle = customRangeTitle ?? "Filter by Custom Range",
            ShowAllTime = showAllTime,
            Url = Url
        });
    }
}

public class DateOptionsToggleModel
{
    private const string SearchTermRouteKey = "searchTerm";

    public SearchString? Search { get; init; }
    public required string Action { get; init; }
    public string? Controller { get; init; }
    public required IDictionary<string, string> RouteValues { get; init; }
    public required string CustomRangeTitle { get; init; }
    public bool ShowAllTime { get; init; }
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

    public string DatePresetUrl(string value)
    {
        var search = SetSearchFilter(SetSearchFilter(Search?.ToString(), "startdate"), "enddate");
        search = SetSearchFilter(search, "startdate", value);
        if (value == "lastmonth")
            search = SetSearchFilter(search, "enddate", value);

        return BuildUrl(search);
    }

    public string AllTimeUrl() =>
        BuildUrl(SetSearchFilter(SetSearchFilter(Search?.ToString(), "startdate"), "enddate"));

    private string BuildUrl(string searchTerm)
    {
        var routeValues = new RouteValueDictionary();
        foreach (var routeValue in RouteValues)
        {
            routeValues[routeValue.Key] = routeValue.Value;
        }

        routeValues[SearchTermRouteKey] = searchTerm;
        return Url.Action(Action, Controller, routeValues) ?? string.Empty;
    }

    private static string SetSearchFilter(string? search, string key, params string[] values)
    {
        var filters = (search ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Where(value => !value.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            filters.Add($"{key}:{value}");
        }

        return filters.Count > 0 ? string.Join(',', filters) : string.Empty;
    }
}
