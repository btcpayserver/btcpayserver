using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Npgsql;

namespace BTCPayServer.Payments.PayJoin
{
    public class UTXOLocker : IUTXOLocker
    {
        private readonly ApplicationDbContextFactory _dbContextFactory;

        public UTXOLocker(ApplicationDbContextFactory dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
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





        private async Task<bool> TryLockInputs(string[] ids)
        {
            using var ctx = _dbContextFactory.CreateContext();
            var connection = ctx.Database.GetDbConnection();
            try
            {
                await connection.ExecuteAsync("""
                INSERT INTO "PayjoinLocks"("Id")
                SELECT * FROM unnest(@ids)
                """, new { ids });
                return true;
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return false;
            }
        }

        public Task<bool> TryLock(OutPoint outpoint)
            => TryLockInputs([outpoint.ToString()]);
        public Task<bool> TryLockInputs(OutPoint[] outPoints)
            => TryLockInputs(outPoints.Select(o => "K-" + o).ToArray());

        public async Task<HashSet<OutPoint>> FindLocks(OutPoint[] outpoints)
        {
            var outPointsStr = outpoints.Select(o => o.ToString());
            await using var ctx = _dbContextFactory.CreateContext();
            return (await ctx.PayjoinLocks.Where(l => outPointsStr.Contains(l.Id)).ToArrayAsync())
                .Select(l => OutPoint.Parse(l.Id)).ToHashSet();
        }
    }
}
