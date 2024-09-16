using System;
using Dapper;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

namespace BTCPayServer.HostedServices
{
    public class CleanupWebhookDeliveriesTask : IPeriodicTask
    {
        public CleanupWebhookDeliveriesTask(ApplicationDbContextFactory dbContextFactory)
        {
            DbContextFactory = dbContextFactory;
        }

        public ApplicationDbContextFactory DbContextFactory { get; }
        public int BatchSize { get; set; } = 500;
        public TimeSpan PruneAfter { get; set; } = TimeSpan.FromDays(60);

        public async Task Do(CancellationToken cancellationToken)
        {
            await using var ctx = DbContextFactory.CreateContext();
            var conn = ctx.Database.GetDbConnection();
            bool pruned = false;
            int offset = 0;
retry:
            var rows = await conn.QueryAsync<WebhookDeliveryData>(@"
SELECT ""Id"", ""Blob""
FROM ""WebhookDeliveries""
WHERE ((now() - ""Timestamp"") > @PruneAfter) AND ""Pruned"" IS FALSE
ORDER BY ""Timestamp""
LIMIT @BatchSize OFFSET @offset
", new { PruneAfter, BatchSize, offset });

            foreach (var d in rows)
            {
                var blob = d.GetBlob();
                blob.Prune();
                d.SetBlob(blob);
                d.Pruned = true;
                pruned = true;
            }
            if (pruned)
            {
                pruned = false;
                await conn.ExecuteAsync("UPDATE \"WebhookDeliveries\" SET \"Blob\"=@Blob::JSONB, \"Pruned\"=@Pruned WHERE \"Id\"=@Id", rows);
                if (rows.Count()  == BatchSize)
                {
                    offset += BatchSize;
                    goto retry;
                }
            }
        }
    }
}
