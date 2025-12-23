#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Migrations;
using BTCPayServer.Services.Invoices;
using Dapper;
using Google.Apis.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static BTCPayServer.Controllers.UIInvoiceController;

namespace BTCPayServer.HostedServices;

public class InvoiceBlobMigratorHostedService : BlobMigratorHostedService<InvoiceData>
{

    private readonly PaymentMethodHandlerDictionary _handlers;

    public InvoiceBlobMigratorHostedService(
        ILogger<InvoiceBlobMigratorHostedService> logs,
        ISettingsRepository settingsRepository,
        ApplicationDbContextFactory applicationDbContextFactory,
        PaymentMethodHandlerDictionary handlers) : base(logs, settingsRepository, applicationDbContextFactory)
    {
        _handlers = handlers;
    }

    public override string SettingsKey => "InvoicesMigration";
    protected override IQueryable<InvoiceData> GetQuery(ApplicationDbContext ctx, DateTimeOffset? progress)
    {
        var query = progress is DateTimeOffset last2 ?
            ctx.Invoices.Include(o => o.Payments).Where(i => i.Created < last2 && i.Currency == null) :
            ctx.Invoices.Include(o => o.Payments).Where(i => i.Currency == null);
        return query.OrderByDescending(i => i.Created);
    }
    protected override Task Reindex(ApplicationDbContext ctx, CancellationToken cancellationToken)
    {
        return ctx.Database.ExecuteSqlRawAsync("REINDEX INDEX \"IX_Invoices_Created\";REINDEX INDEX \"PK_Invoices\";", cancellationToken);
    }
    protected override DateTimeOffset ProcessEntities(ApplicationDbContext ctx, List<InvoiceData> invoices)
    {
        // Those clean up the JSON blobs
        foreach (var inv in invoices)
        {
            var blob = inv.GetBlob();
            var prompts = blob.GetPaymentPrompts();
            foreach (var p in prompts)
            {
                if (_handlers.TryGetValue(p.PaymentMethodId, out var handler) && p.Details is not (null or { Type: JTokenType.Null }))
                {
                    p.Details = JToken.FromObject(handler.ParsePaymentPromptDetails(p.Details), handler.Serializer);
                }
            }
            blob.SetPaymentPrompts(prompts);
            inv.SetBlob(blob);
            foreach (var pay in inv.Payments)
            {
                var paymentEntity = pay.GetBlob();
                if (_handlers.TryGetValue(paymentEntity.PaymentMethodId, out var handler) && paymentEntity.Details is not (null or { Type: JTokenType.Null }))
                {
                    paymentEntity.Details = JToken.FromObject(handler.ParsePaymentDetails(paymentEntity.Details), handler.Serializer);
                }
                pay.SetBlob(paymentEntity);

                if (pay.MigratedPaymentMethodId is not null && pay.PaymentMethodId != pay.MigratedPaymentMethodId)
                {
                    ctx.Add(pay);
                    ctx.Payments.Remove(new PaymentData() { Id = pay.Id, PaymentMethodId = pay.MigratedPaymentMethodId });
                }
            }
        }
        return invoices[^1].Created;
    }

    protected override async Task PostMigrationCleanup(ApplicationDbContext ctx, CancellationToken cancellationToken)
    {
        Logs.LogInformation("Post-migration VACUUM (ANALYZE)");
        await ctx.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) \"Invoices\"", cancellationToken);
        Logs.LogInformation("Post-migration VACUUM (ANALYZE) finished");
    }
}
