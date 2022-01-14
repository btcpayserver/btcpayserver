using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Security.Bitpay
{
    public enum PairingResult
    {
        Partial,
        Complete,
        ReusedKey,
        Expired
    }

    public class TokenRepository
    {
        readonly ApplicationDbContextFactory _Factory;
        public TokenRepository(ApplicationDbContextFactory dbFactory)
        {
            ArgumentNullException.ThrowIfNull(dbFactory);
            _Factory = dbFactory;
        }

        public async Task<BitTokenEntity[]> GetTokens(string sin)
        {
            if (sin == null)
                return Array.Empty<BitTokenEntity>();
            using var ctx = _Factory.CreateContext();
            return (await ctx.PairedSINData.Where(p => p.SIN == sin)
                .ToArrayAsync())
                .Select(p => CreateTokenEntity(p))
                .ToArray();
        }

        public async Task<String> GetStoreIdFromAPIKey(string apiKey)
        {
            using var ctx = _Factory.CreateContext();
            return await ctx.ApiKeys.Where(o => o.Id == apiKey).Select(o => o.StoreId).FirstOrDefaultAsync();
        }

        public async Task GenerateLegacyAPIKey(string storeId)
        {
            // It is legacy support and Bitpay generate string of unknown format, trying to replicate them
            // as good as possible. The string below got generated for me.
            var chars = "ERo0vkBMOYhyU0ZHvirCplbLDIGWPdi1ok77VnW7QdE";
            var generated = new char[chars.Length];
            for (int i = 0; i < generated.Length; i++)
            {
                generated[i] = chars[(int)(RandomUtils.GetUInt32() % generated.Length)];
            }

            using var ctx = _Factory.CreateContext();
            var existing = await ctx.ApiKeys.Where(o => o.StoreId == storeId && o.Type == APIKeyType.Legacy).ToListAsync();
            if (existing.Any())
            {
                ctx.ApiKeys.RemoveRange(existing);
            }
            ctx.ApiKeys.Add(new APIKeyData() { Id = new string(generated), StoreId = storeId });
            await ctx.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task RevokeLegacyAPIKeys(string storeId)
        {
            var keys = await GetLegacyAPIKeys(storeId);
            if (!keys.Any())
            {
                return;
            }

            using var ctx = _Factory.CreateContext();
            ctx.ApiKeys.RemoveRange(keys.Select(s => new APIKeyData() { Id = s }));
            await ctx.SaveChangesAsync();
        }

        public async Task<string[]> GetLegacyAPIKeys(string storeId)
        {
            using var ctx = _Factory.CreateContext();
            return await ctx.ApiKeys.Where(o => o.StoreId == storeId && o.Type == APIKeyType.Legacy).Select(c => c.Id).ToArrayAsync();
        }

        private BitTokenEntity CreateTokenEntity(PairedSINData data)
        {
            return new BitTokenEntity()
            {
                Label = data.Label,
                Value = data.Id,
                SIN = data.SIN,
                PairingTime = data.PairingTime,
                StoreId = data.StoreDataId
            };
        }

        public async Task<string> CreatePairingCodeAsync()
        {
            string pairingCodeId = null;
            while (true)
            {
                pairingCodeId = Encoders.Base58.EncodeData(RandomUtils.GetBytes(6));
                if (pairingCodeId.Length == 7) // woocommerce plugin check for exactly 7 digits
                    break;
            }
            using (var ctx = _Factory.CreateContext())
            {
                var now = DateTime.UtcNow;
                var expiration = DateTime.UtcNow + TimeSpan.FromMinutes(15);
                ctx.PairingCodes.Add(new PairingCodeData()
                {
                    Id = pairingCodeId,
                    DateCreated = now,
                    Expiration = expiration,
                    TokenValue = Encoders.Base58.EncodeData(RandomUtils.GetBytes(32))
                });
                await ctx.SaveChangesAsync();
            }
            return pairingCodeId;
        }

        public async Task<PairingCodeEntity> UpdatePairingCode(PairingCodeEntity pairingCodeEntity)
        {
            using var ctx = _Factory.CreateContext();
            var pairingCode = await ctx.PairingCodes.FindAsync(pairingCodeEntity.Id);
            pairingCode.Label = pairingCodeEntity.Label;
            await ctx.SaveChangesAsync();
            return CreatePairingCodeEntity(pairingCode);
        }

        public async Task<PairingResult> PairWithStoreAsync(string pairingCodeId, string storeId)
        {
            using var ctx = _Factory.CreateContext();
            var pairingCode = await ctx.PairingCodes.FindAsync(pairingCodeId);
            if (pairingCode == null || pairingCode.Expiration < DateTimeOffset.UtcNow)
                return PairingResult.Expired;
            pairingCode.StoreDataId = storeId;
            var result = await ActivateIfComplete(ctx, pairingCode);
            await ctx.SaveChangesAsync();
            return result;
        }

        public async Task<PairingResult> PairWithSINAsync(string pairingCodeId, string sin)
        {
            using var ctx = _Factory.CreateContext();
            var pairingCode = await ctx.PairingCodes.FindAsync(pairingCodeId);
            if (pairingCode == null || pairingCode.Expiration < DateTimeOffset.UtcNow)
                return PairingResult.Expired;
            pairingCode.SIN = sin;
            var result = await ActivateIfComplete(ctx, pairingCode);
            await ctx.SaveChangesAsync();
            return result;
        }


        private async Task<PairingResult> ActivateIfComplete(ApplicationDbContext ctx, PairingCodeData pairingCode)
        {
            if (!string.IsNullOrEmpty(pairingCode.SIN) && !string.IsNullOrEmpty(pairingCode.StoreDataId))
            {
                ctx.PairingCodes.Remove(pairingCode);

                // Can have concurrency issues... but no harm can be done
                var alreadyUsed = await ctx.PairedSINData.Where(p => p.SIN == pairingCode.SIN && p.StoreDataId != pairingCode.StoreDataId).AnyAsync();
                if (alreadyUsed)
                    return PairingResult.ReusedKey;
                await ctx.PairedSINData.AddAsync(new PairedSINData()
                {
                    Id = pairingCode.TokenValue,
                    PairingTime = DateTime.UtcNow,
                    Label = pairingCode.Label,
                    StoreDataId = pairingCode.StoreDataId,
                    SIN = pairingCode.SIN
                });
                return PairingResult.Complete;
            }
            return PairingResult.Partial;
        }


        public async Task<BitTokenEntity[]> GetTokensByStoreIdAsync(string storeId)
        {
            using var ctx = _Factory.CreateContext();
            return (await ctx.PairedSINData.Where(p => p.StoreDataId == storeId).ToListAsync())
                    .Select(c => CreateTokenEntity(c))
                    .ToArray();
        }

        public async Task<PairingCodeEntity> GetPairingAsync(string pairingCode)
        {
            using var ctx = _Factory.CreateContext();
            return CreatePairingCodeEntity(await ctx.PairingCodes.FindAsync(pairingCode));
        }

        private PairingCodeEntity CreatePairingCodeEntity(PairingCodeData data)
        {
            if (data == null)
                return null;
            return new PairingCodeEntity()
            {
                Id = data.Id,
                Label = data.Label,
                Expiration = data.Expiration,
                CreatedTime = data.DateCreated,
                TokenValue = data.TokenValue,
                SIN = data.SIN
            };
        }


        public async Task<bool> DeleteToken(string tokenId)
        {
            using var ctx = _Factory.CreateContext();
            var token = await ctx.PairedSINData.FindAsync(tokenId);
            if (token == null)
                return false;
            ctx.PairedSINData.Remove(token);
            await ctx.SaveChangesAsync();
            return true;
        }

        public async Task<BitTokenEntity> GetToken(string tokenId)
        {
            using var ctx = _Factory.CreateContext();
            var token = await ctx.PairedSINData.FindAsync(tokenId);
            if (token == null)
                return null;
            return CreateTokenEntity(token);
        }

    }
}
