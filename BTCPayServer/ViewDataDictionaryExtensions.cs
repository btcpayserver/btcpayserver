#nullable enable
using System;
using BTCPayServer.Components.DateFormatterOptions;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer;

public static class ViewDataDictionaryExtensions
{
    public static void SetDateFormatterOptions(this ViewDataDictionary viewData, DateFormatterOptions? options)
    => viewData["DateFormatterOptions"] = options;

    public static void SetTimeZone(this ViewDataDictionary viewData, TimeZoneInfo timeZone)
    => SetTimeZone(viewData, timeZone.Id);
    public static void SetTimeZone(this ViewDataDictionary viewData, string timeZone)
    {
        var o = viewData.GetDateFormatterOptions() ?? new();
        o.TimeZone = timeZone;
        viewData.SetDateFormatterOptions(o);
    }

    public static string? GetTimeZone(this ViewDataDictionary viewData)
    => viewData.GetDateFormatterOptions()?.TimeZone;

    public static DateFormatterOptions? GetDateFormatterOptions(this ViewDataDictionary viewData)
    => viewData["DateFormatterOptions"] as DateFormatterOptions;
}
