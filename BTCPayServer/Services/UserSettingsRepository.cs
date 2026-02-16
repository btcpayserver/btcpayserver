#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace BTCPayServer.Services;

public class UserSettingsRepository
{
    private readonly ApplicationDbContextFactory _contextFactory;

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
    };

    public UserSettingsRepository(ApplicationDbContextFactory contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<T?> GetSettingAsync<T>(string userId, string name) where T : class
    {
        await using var ctx = _contextFactory.CreateContext();
        var data = await ctx.UserSettings
            .Where(s => s.Name == name && s.UserId == userId)
            .FirstOrDefaultAsync();
        return data == null ? default : Deserialize<T>(data.Value);
    }

    public async Task<Dictionary<string, T?>> GetSettingsAsync<T>(string name) where T : class
    {
        await using var ctx = _contextFactory.CreateContext();
        var data = await ctx.UserSettings
            .Where(s => s.Name == name)
            .ToDictionaryAsync(s => s.UserId);
        return data.ToDictionary(pair => pair.Key, pair => Deserialize<T>(pair.Value.Value));
    }

    public async Task UpdateSetting<T>(string userId, string name, T? obj) where T : class
    {
        await using var ctx = _contextFactory.CreateContext();
        UserSettingData? settings = null;
        if (obj is null)
        {
            ctx.UserSettings.RemoveRange(
                ctx.UserSettings.Where(data => data.Name == name && data.UserId == userId));
        }
        else
        {
            settings = new UserSettingData
            {
                Name = name,
                UserId = userId,
                Value = Serialize(obj)
            };
            ctx.Attach(settings);
            ctx.Entry(settings).State = EntityState.Modified;
        }
        try
        {
            await ctx.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (settings is not null)
            {
                ctx.Entry(settings).State = EntityState.Added;
                await ctx.SaveChangesAsync();
            }
        }
    }

    private static T? Deserialize<T>(string value) where T : class
    {
        return JsonConvert.DeserializeObject<T>(value, SerializerSettings);
    }

    private static string Serialize<T>(T obj)
    {
        return JsonConvert.SerializeObject(obj, SerializerSettings);
    }
}
