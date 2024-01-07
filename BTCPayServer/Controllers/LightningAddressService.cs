#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BTCPayServer;

public class LightningAddressService
{
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly IMemoryCache _memoryCache;

    public LightningAddressService(ApplicationDbContextFactory applicationDbContextFactory, IMemoryCache memoryCache)
    {
        _applicationDbContextFactory = applicationDbContextFactory;
        _memoryCache = memoryCache;
    }

    public async Task<List<LightningAddressData>> Get(LightningAddressQuery query)
    {
        await using var context = _applicationDbContextFactory.CreateContext();
        return await GetCore(context, query);
    }

    private async Task<List<LightningAddressData>> GetCore(ApplicationDbContext context, LightningAddressQuery query)
    {
        IQueryable<LightningAddressData?> queryable = context.LightningAddresses.AsQueryable();
        query.Usernames = query.Usernames?.Select(NormalizeUsername)?.ToArray();
        if (query.Usernames is not null)
        {
            queryable = queryable.Where(data => query.Usernames.Contains(data!.Username));
        }

        if (query.StoreIds is not null)
        {
            queryable = queryable.Where(data => query.StoreIds.Contains(data!.StoreDataId));
        }

#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
        return await queryable.ToListAsync();
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
    }

    public async Task<LightningAddressData?> ResolveByAddress(string username)
    {
        return await _memoryCache.GetOrCreateAsync(GetKey(username), async entry =>
        {
            var result = await Get(new LightningAddressQuery { Usernames = new[] { NormalizeUsername(username) } });
            return result.FirstOrDefault();
        });
    }

    public async Task<bool> Set(LightningAddressData data)
    {
        data.Username = NormalizeUsername(data.Username);
        await using var context = _applicationDbContextFactory.CreateContext();
        var result = (await GetCore(context, new LightningAddressQuery { Usernames = new[] { data.Username } }))
            .FirstOrDefault();
        if (result is not null)
        {
            if (result.StoreDataId != data.StoreDataId)
            {
                return false;
            }

            context.Remove(result);
        }

        await context.AddAsync(data);
        await context.SaveChangesAsync();
        _memoryCache.Remove(GetKey(data.Username));
        return true;
    }

    public async Task<bool> Remove(string username, string? storeId = null)
    {
        username = NormalizeUsername(username);
        await using var context = _applicationDbContextFactory.CreateContext();
        var x = (await GetCore(context, new LightningAddressQuery { Usernames = new[] { username } })).FirstOrDefault();
        if (x is null)
            return true;
        if (storeId is not null && x.StoreDataId != storeId)
        {
            return false;
        }

        context.Remove(x);
        await context.SaveChangesAsync();
        _memoryCache.Remove(GetKey(username));
        return true;
    }

    public async Task Set(LightningAddressData data, ApplicationDbContext context)
    {
        var result = (await GetCore(context, new LightningAddressQuery { Usernames = new[] { data.Username } }))
            .FirstOrDefault();
        if (result is not null)
        {
            if (result.StoreDataId != data.StoreDataId)
            {
                return;
            }

            context.Remove(result);
        }

        await context.AddAsync(data);
    }

    public static string NormalizeUsername(string username)
    {
        return username.ToLowerInvariant();
    }

    private static string GetKey(string username)
    {
        username = NormalizeUsername(username);
        return $"{nameof(LightningAddressService)}_{username}";
    }
}
