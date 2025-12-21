using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Migrations;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices
{
    public class PaymentRequestsMigratorHostedService : BlobMigratorHostedService<PaymentRequestData>
    {
        private readonly PaymentRequestStreamer _paymentRequestStreamer;

        public PaymentRequestsMigratorHostedService(
       ILogger<PaymentRequestsMigratorHostedService> logs,
       ISettingsRepository settingsRepository,
       PaymentRequestStreamer paymentRequestStreamer,
       ApplicationDbContextFactory applicationDbContextFactory) : base(logs, settingsRepository, applicationDbContextFactory)
        {
            _paymentRequestStreamer = paymentRequestStreamer;
        }
        public override string SettingsKey => "PaymentRequestsMigration2";

        protected override IQueryable<PaymentRequestData> GetQuery(ApplicationDbContext ctx, DateTimeOffset? progress)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var query = progress is DateTimeOffset last2 ?
            ctx.PaymentRequests.Where(i => i.Created < last2 && !((i.Blob == null || i.Blob.Length == 0) && i.Blob2 != null && i.Currency != null)) :
            ctx.PaymentRequests.Where(i => !((i.Blob == null || i.Blob.Length == 0) && i.Blob2 != null && i.Currency != null));
            return query.OrderByDescending(i => i.Created);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        protected override async Task PostMigrationCleanup(ApplicationDbContext ctx, CancellationToken cancellationToken)
        {
            Logs.LogInformation("Post-migration VACUUM (FULL, ANALYZE)");
            await ctx.Database.ExecuteSqlRawAsync("VACUUM (FULL, ANALYZE) \"PaymentRequests\"", cancellationToken);
            Logs.LogInformation("Post-migration VACUUM (FULL, ANALYZE) finished");
            _paymentRequestStreamer.CheckExpirable();
        }

        protected override DateTimeOffset ProcessEntities(ApplicationDbContext ctx, List<PaymentRequestData> entities)
        {
            // The PaymentRequestData.Migrate() is automatically called by EF.
            // But Modified isn't set as it happens before the ctx is bound to the entity.
            foreach (var entity in entities)
            {
                // Make sure the blob is clean
                entity.SetBlob(entity.GetBlob());
                ctx.PaymentRequests.Entry(entity).State = EntityState.Modified;
            }
            return entities[^1].Created;
        }

        protected override Task Reindex(ApplicationDbContext ctx, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
