#nullable enable
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer;

public static class ViewDataDictionaryExtensions
{
    public static string? GetPageTimeZone(this ViewDataDictionary viewData)
        => viewData["timezone"] as string;
    /// <summary>
    /// Set the timezone of the current page. If null, the timezone of the browser will be used.
    /// </summary>
    /// <param name="viewData"></param>
    /// <param name="timezone"></param>
    public static void SetPageTimeZone(this ViewDataDictionary viewData, string? timezone)
        => viewData["timezone"] = timezone;

    /// <summary>
    /// Set the timezone of the current page from the SearchString if it includes a `timezone=` filter.
    /// </summary>
    /// <param name="viewData"></param>
    /// <param name="searchString"></param>
    public static void SetPageTimeZone(this ViewDataDictionary viewData, SearchString? searchString)
    {
        if (searchString?.GetExplicitTimeZone() is string timezone)
            viewData["timezone"] = timezone;
    }
}
