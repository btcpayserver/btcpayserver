#nullable enable
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer;

public static class ViewDataDictionaryExtensions
{
    public static void SetDateFormatterOptions(this ViewDataDictionary viewData, DateFormatterOptionsProvider dateFormatterOptionsProvider, StoreData store)
    {
        SetDateFormatterOptions(viewData, dateFormatterOptionsProvider.GetStoreDateFormatterOptions(store));
    }

    public static void SetDateFormatterOptions(this ViewDataDictionary viewData, DateFormatterOptions? options)
    => viewData["DateFormatterOptions"] = options;

    public static string? GetTimeZone(this ViewDataDictionary viewData)
    => viewData.GetDateFormatterOptions()?.TimeZone;

    public static DateFormatterOptions? GetDateFormatterOptions(this ViewDataDictionary viewData)
    => viewData["DateFormatterOptions"] as DateFormatterOptions;
}
