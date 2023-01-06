using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Payments.PayJoin
{
    public class UTXOLocker : IUTXOLocker
    {
        private readonly ApplicationDbContextFactory _dbContextFactory;

        public UTXOLocker(ApplicationDbContextFactory dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<bool> TryLock(OutPoint outpoint)
        {
            using var ctx = _dbContextFactory.CreateContext();
            ctx.PayjoinLocks.Add(new PayjoinLock() { Id = outpoint.ToString() });
            try
            {
                return await ctx.SaveChangesAsync() == 1;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        public async Task<bool> TryUnlock(params OutPoint[] outPoints)
        {
            using var ctx = _dbContextFactory.CreateContext();
            foreach (OutPoint outPoint in outPoints)
            {
                ctx.PayjoinLocks.Remove(new PayjoinLock() { Id = outPoint.ToString() });
            }

            try
            {
                return await ctx.SaveChangesAsync() == outPoints.Length;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        public async Task<bool> TryLockInputs(OutPoint[] outPoints)
        {
            using var ctx = _dbContextFactory.CreateContext();
            foreach (OutPoint outPoint in outPoints)
            {
                ctx.PayjoinLocks.Add(new PayjoinLock()
                {
                    // Random flag so it does not lock same id
                    // as the lock utxo
                    Id = "K-" + outPoint.ToString()
                });
            }

            try
            {
                return await ctx.SaveChangesAsync() == outPoints.Length;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        public async Task<HashSet<OutPoint>> FindLocks(OutPoint[] outpoints)
        {
            var outPointsStr = outpoints.Select(o => o.ToString());
            await using var ctx = _dbContextFactory.CreateContext();
            return (await ctx.PayjoinLocks.Where(l => outPointsStr.Contains(l.Id)).ToArrayAsync())
                .Select(l => OutPoint.Parse(l.Id)).ToHashSet();
        }
    }
}
