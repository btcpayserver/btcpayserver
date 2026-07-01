#nullable enable
using System;
using System.Security.Principal;
using System.Threading.Tasks;
using BTCPayServer.Data;

namespace BTCPayServer.Services;

public class DateFormatterOptionsProvider(
    ISettingsAccessor<PoliciesSettings> settingsAccessor,
    ApplicationDbContextFactory applicationDbContextFactory)
{
    public TimeZoneInfo GetServerTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(GetServerDateFormatterOptions().TimeZone);

    public TimeZoneInfo GetStoreTimeZone(StoreData store) => TimeZoneInfo.FindSystemTimeZoneById(GetStoreDateFormatterOptions(store).TimeZone);

    public TimeZoneInfo GetUserTimeZone(ApplicationUser user) => TimeZoneInfo.FindSystemTimeZoneById(user.TimeZone ?? settingsAccessor.Settings.ServerTimeZone ?? TimeZoneInfo.Utc.Id);

    public async Task<TimeZoneInfo> GetUserTimeZone(IPrincipal user)
    {
        await using var context = applicationDbContextFactory.CreateContext();
        var userId = user.GetId();
        var userEntity = await context.Users.FindAsync(userId ?? "");
        if (userEntity is null)
            return GetServerTimeZone();
        return GetUserTimeZone(userEntity);
    }

    public DateFormatterOptions GetServerDateFormatterOptions() => new()
    {
        TimeZone = settingsAccessor.Settings.ServerTimeZone ?? TimeZoneInfo.Utc.Id,
        Locale = settingsAccessor.Settings.ServerLocale ?? "en-US",
        DateStyle = "medium",
        TimeStyle = "short",
        Hour12 = true
    };

    public DateFormatterOptions GetStoreDateFormatterOptions(StoreData store)
    {
        var server = GetServerDateFormatterOptions();
        var storeBlob = store.GetStoreBlob();
        return new()
        {
            TimeZone = store.TimeZone ?? server.TimeZone,
            Locale = storeBlob.PreferredDateTimeLocale ?? server.Locale,
            DateStyle = storeBlob.PreferredDateStyle ?? server.DateStyle,
            TimeStyle = storeBlob.PreferredTimeStyle ?? server.TimeStyle,
            Hour12 = storeBlob.PreferredHour12
        };
    }
}
