#nullable enable
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer;

public static class ViewDataDictionaryExtensions
{
    public static void SetDateFormatterOptions(this ViewDataDictionary viewData, TimeZoneProvider timeZoneProvider, StoreData store)
    {
        var dateFormatterOptions = new DateFormatterOptions();
        dateFormatterOptions.TimeZone = timeZoneProvider.GetStoreTimeZone(store).Id;
        if (DateFormatterOptions.GetTemplate(store.GetStoreBlob().PreferredDateFormat) is { } template)
        {
            dateFormatterOptions = dateFormatterOptions.Merge(template.DateFormatOptions);
        }
        if (DateFormatterOptions.GetTimeTemplate(store.GetStoreBlob().PreferredTimeFormat) is { } timeTemplate)
        {
            dateFormatterOptions = dateFormatterOptions.Merge(timeTemplate.DateFormatOptions);
        }
        SetDateFormatterOptions(viewData, dateFormatterOptions);
    }

    public static void SetDateFormatterOptions(this ViewDataDictionary viewData, DateFormatterOptions? options)
    => viewData["DateFormatterOptions"] = options;

    public static string? GetTimeZone(this ViewDataDictionary viewData)
    => viewData.GetDateFormatterOptions()?.TimeZone;

    public static DateFormatterOptions? GetDateFormatterOptions(this ViewDataDictionary viewData)
    => viewData["DateFormatterOptions"] as DateFormatterOptions;
}
