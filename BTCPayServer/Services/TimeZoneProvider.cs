#nullable enable
using System;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using BTCPayServer.Data;

namespace BTCPayServer.Services;

public class TimeZoneProvider(
    ISettingsAccessor<PoliciesSettings> settingsAccessor,
    ApplicationDbContextFactory applicationDbContextFactory)
{
    public TimeZoneInfo GetServerTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(settingsAccessor.Settings.ServerTimeZone ?? "UTC");
    public TimeZoneInfo GetStoreTimeZone(StoreData store) => TimeZoneInfo.FindSystemTimeZoneById(store.TimeZone ?? settingsAccessor.Settings.ServerTimeZone ?? "UTC");
    public TimeZoneInfo GetUserTimeZone(ApplicationUser user) => TimeZoneInfo.FindSystemTimeZoneById(user.TimeZone ?? settingsAccessor.Settings.ServerTimeZone  ?? "UTC");

    public async Task<TimeZoneInfo> GetUserTimeZone(IPrincipal user)
    {
        await using var context = applicationDbContextFactory.CreateContext();
        var userId = user.GetId();
        var userEntity = await context.Users.FindAsync(userId ?? "");
        if (userEntity is null)
            return GetServerTimeZone();
        return GetUserTimeZone(userEntity);
    }
}
