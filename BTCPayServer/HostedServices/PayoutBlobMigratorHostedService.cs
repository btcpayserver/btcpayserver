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
using BTCPayServer.Services.Invoices;
using Google.Apis.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static BTCPayServer.Controllers.UIInvoiceController;

namespace BTCPayServer.HostedServices;

public class PayoutBlobMigratorHostedService : BlobMigratorHostedService<PayoutData>
{
    
    private readonly PaymentMethodHandlerDictionary _handlers;

    public PayoutBlobMigratorHostedService(
        ILogger<PayoutBlobMigratorHostedService> logs,
        ISettingsRepository settingsRepository,
        ApplicationDbContextFactory applicationDbContextFactory,
        PaymentMethodHandlerDictionary handlers) : base(logs, settingsRepository, applicationDbContextFactory)
    {
        _handlers = handlers;
    }

    public override string SettingsKey => "PayoutsMigration";
    protected override IQueryable<PayoutData> GetQuery(ApplicationDbContext ctx, DateTimeOffset? progress)
    {
        var query = progress is DateTimeOffset last2 ?
            ctx.Payouts.Where(i => i.Date < last2 && i.Currency == null) :
            ctx.Payouts.Where(i => i.Currency == null);
        return query.OrderByDescending(i => i);
    }
    protected override DateTimeOffset ProcessEntities(ApplicationDbContext ctx, List<PayoutData> payouts)
    {
        foreach (var entry in ctx.ChangeTracker.Entries<PayoutData>())
        {
            entry.State = EntityState.Modified;
        }
        return payouts[^1].Date;
    }
}
