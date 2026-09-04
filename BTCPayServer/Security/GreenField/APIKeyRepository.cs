using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;
using Npgsql;

namespace BTCPayServer.Security.Greenfield
{
    public class APIKeyRepository(ApplicationDbContextFactory applicationDbContextFactory)
    {
        public static APIKeyData New()
        {
            APIKeyData k = new()
            {
                Key = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20))
            };
            var apiKey = new Selector.ByApiKey(k.Key);
            k.Hash = apiKey.GetHash();
            k.Id = apiKey.GetId();
            k.Prefix = k.Key.Substring(0, 6);
            return k;
        }

        public abstract record Selector
        {
            public abstract string GetId();

            public record ById(string ApiKeyId) : Selector
            {
                public override string GetId() => APIKeyData.IsId(ApiKeyId) ? ApiKeyId : "???";
            }

            public record ByApiKey(string ApiKey) : Selector
            {
                public string GetHash()
                {
                    try
                    {
                        return Encoders.Hex.EncodeData(SHA256.HashData(Encoding.UTF8.GetBytes(ApiKey)));
                    }
                    catch
                    {
                        return "???";
                    }
                }
                public override string GetId()
                {
                    try
                    {
                        var hash = SHA256.HashData(SHA256.HashData(Encoding.UTF8.GetBytes(ApiKey)));
                        return APIKeyData.IdPrefix + "_" + Encoders.Hex.EncodeData(hash)[..16];
                    }
                    catch
                    {
                        return "???";
                    }
                }
            }

        }

        public async Task<APIKeyData> GetKey(Selector selector, bool includeUser = false)
        {
            var id = selector.GetId();
            await using var context = applicationDbContextFactory.CreateContext();
            var result = includeUser ?
                await context.ApiKeys.Include(data => data.User).SingleOrDefaultAsync(data => data.Id == id) :
                await context.ApiKeys.SingleOrDefaultAsync(data => data.Id == id);
            if (result != null && selector is Selector.ByApiKey apiKey && apiKey.GetHash() != result.Hash)
                result = null;
            return result;
        }

        public async Task<List<APIKeyData>> GetKeys(APIKeyQuery query)
        {
            using var context = applicationDbContextFactory.CreateContext();
            var queryable = context.ApiKeys.AsQueryable();
            if (query != null)
            {
                if (query.UserId != null && query.UserId.Any())
                {
                    queryable = queryable.Where(data => query.UserId.Contains(data.UserId));
                }
            }

            return await queryable.ToListAsync();
        }

        public async Task CreateKey(APIKeyData key)
        {
            using var context = applicationDbContextFactory.CreateContext();
            await context.ApiKeys.AddAsync(key);
            await context.SaveChangesAsync();
        }

        public async Task<bool> Remove(Selector selector, string getUserId)
        {
            var id = selector.GetId();
            using (var context = applicationDbContextFactory.CreateContext())
            {
                var key = await EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(context.ApiKeys,
                    data => data.Id == id && data.UserId == getUserId);
                if (key == null)
                    return false;
                await context.ApiKeyPermissionUsages.Where(u => u.ApiKeyId == id).ExecuteDeleteAsync();
                context.ApiKeys.Remove(key);
                await context.SaveChangesAsync();
            }
            return true;
        }

        public async Task RecordPermissionUsage(Selector selector, Permission permission)
        {
            var id = selector.GetId();
            using var context = applicationDbContextFactory.CreateContext();
            var sql = @"
            INSERT INTO ""ApiKeyPermissionUsages"" (""Id"", ""ApiKeyId"", ""Permission"", ""LastUsed"", ""UsageCount"")
            VALUES (@Id, @ApiKeyId, @Permission, @LastUsed, 1)
            ON CONFLICT (""Id"")
            DO UPDATE SET
                ""LastUsed"" = @LastUsed,
                ""UsageCount"" = ""ApiKeyPermissionUsages"".""UsageCount"" + 1";

            await context.Database.ExecuteSqlRawAsync(sql,
                new NpgsqlParameter("@Id", $"{id}-{permission}"),
                new NpgsqlParameter("@ApiKeyId", id),
                new NpgsqlParameter("@Permission", permission.Policy),
                new NpgsqlParameter("@LastUsed", DateTimeOffset.UtcNow));
        }

        public async Task<List<ApiKeyPermissionUsage>> GetAPIPermissionUsageRecords(Selector selector)
        {
            var id = selector.GetId();
            await using var ctx = applicationDbContextFactory.CreateContext();
            var entity = ctx.ApiKeyPermissionUsages.Where(c => c.ApiKeyId == id).ToList();
            return entity.Any() ? entity : new List<ApiKeyPermissionUsage>();
        }

        public class APIKeyQuery
        {
            public string[] UserId { get; set; }
        }
    }
}
