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

public class InvoiceBlobMigratorHostedService : IHostedService
{
    const string SettingsKey = "InvoiceBlobMigratorHostedService.Settings";
    private readonly PaymentMethodHandlerDictionary _handlers;

    internal class Settings
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? Progress { get; set; }
        public bool Complete { get; set; }
    }
    Task? _Migrating;
    TaskCompletionSource _Cts = new TaskCompletionSource();
    public InvoiceBlobMigratorHostedService(
        ILogger<InvoiceBlobMigratorHostedService> logs,
        ISettingsRepository settingsRepository,
        ApplicationDbContextFactory applicationDbContextFactory,
        PaymentMethodHandlerDictionary handlers)
    {
        Logs = logs;
        SettingsRepository = settingsRepository;
        ApplicationDbContextFactory = applicationDbContextFactory;
        _handlers = handlers;
    }

    public ILogger<InvoiceBlobMigratorHostedService> Logs { get; }
    public ISettingsRepository SettingsRepository { get; }
    public ApplicationDbContextFactory ApplicationDbContextFactory { get; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _Migrating = Migrate(cancellationToken);
        return Task.CompletedTask;
    }
    public int BatchSize { get; set; } = 1000;

    private async Task Migrate(CancellationToken cancellationToken)
    {
        var settings = await SettingsRepository.GetSettingAsync<Settings>(SettingsKey) ?? new Settings();
        if (settings.Complete is true)
            return;
        if (settings.Progress is DateTimeOffset last)
            Logs.LogInformation($"Migrating invoices JSON Blobs from {last}");
        else
            Logs.LogInformation("Migrating invoices JSON Blobs from the beginning");

        int batchSize = BatchSize;
        while (!cancellationToken.IsCancellationRequested)
        {
retry:
            List<InvoiceData> invoices;
            await using (var ctx = ApplicationDbContextFactory.CreateContext())
            {
                var query = settings.Progress is DateTimeOffset last2 ?
                    ctx.Invoices.Include(o => o.Payments).Where(i => i.Created < last2 && i.Currency == null) :
                    ctx.Invoices.Include(o => o.Payments).Where(i => i.Currency == null);
                query = query.OrderByDescending(i => i.Created).Take(batchSize);
                invoices = await query.ToListAsync(cancellationToken);
                if (invoices.Count == 0)
                {
                    await SettingsRepository.UpdateSetting<Settings>(new Settings() { Complete = true }, SettingsKey);
                    Logs.LogInformation("Migration of invoices JSON Blobs completed");
                    return;
                }

                try
                {
                    // Those clean up the JSON blobs, and mark entities as modified
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
                        }
                    }
                    foreach (var entry in ctx.ChangeTracker.Entries<InvoiceData>())
                    {
                        entry.State = EntityState.Modified;
                    }
                    foreach (var entry in ctx.ChangeTracker.Entries<PaymentData>())
                    {
                        entry.State = EntityState.Modified;
                    }
                    await ctx.SaveChangesAsync();
                    batchSize = BatchSize;
                }
                catch (DbUpdateConcurrencyException)
                {
                    batchSize /= 2;
                    batchSize = Math.Max(1, batchSize);
                    goto retry;
                }
            }
            settings = new Settings() { Progress = invoices[^1].Created };
            await SettingsRepository.UpdateSetting<Settings>(settings, SettingsKey);
        }
    }

    public async Task ResetMigration()
    {
        await SettingsRepository.UpdateSetting<Settings>(new Settings(), SettingsKey);
    }
    public async Task<bool> IsComplete()
    {
        return (await SettingsRepository.GetSettingAsync<Settings>(SettingsKey)) is { Complete: true };
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _Cts.TrySetCanceled();
        return (_Migrating ?? Task.CompletedTask).ContinueWith(t =>
        {
            if (t.IsFaulted)
                Logs.LogError(t.Exception, "Error while migrating invoices JSON Blobs");
        });
    }
}
