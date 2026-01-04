#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static BTCPayServer.Controllers.UIInvoiceController;

namespace BTCPayServer.HostedServices;

public abstract class BlobMigratorHostedService<TEntity> : IHostedService
{
    public abstract string SettingsKey { get; }
    internal class Settings
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? Progress { get; set; }
        public bool Complete { get; set; }
    }
    Task? _Migrating;
    CancellationTokenSource? _Cts;
    public BlobMigratorHostedService(
        ILogger logs,
        ISettingsRepository settingsRepository,
        ApplicationDbContextFactory applicationDbContextFactory)
    {
        Logs = logs;
        SettingsRepository = settingsRepository;
        ApplicationDbContextFactory = applicationDbContextFactory;
    }

    public ILogger Logs { get; }
    public ISettingsRepository SettingsRepository { get; }
    public ApplicationDbContextFactory ApplicationDbContextFactory { get; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _Cts = new CancellationTokenSource();
        _Migrating = Migrate(_Cts.Token);
        return Task.CompletedTask;
    }
    public int BatchSize { get; set; } = 1000;

    private async Task Migrate(CancellationToken cancellationToken)
    {
        var settings = await SettingsRepository.GetSettingAsync<Settings>(SettingsKey) ?? new Settings();
        if (settings.Complete is true)
            return;
        if (settings.Progress is DateTimeOffset last)
            Logs.LogInformation($"Migrating from {last}");
        else
            Logs.LogInformation("Migrating from the beginning");


        int batchSize = BatchSize;
retry:
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                List<TEntity> entities;
                DateTimeOffset progress;
                await using (var ctx = ApplicationDbContextFactory.CreateContext(o => o.CommandTimeout((int)TimeSpan.FromDays(1.0).TotalSeconds)))
                {
                    var query = GetQuery(ctx, settings?.Progress).Take(batchSize);
                    entities = await query.ToListAsync(cancellationToken);
                    if (entities.Count == 0)
                    {
                        var count = await GetQuery(ctx, null).CountAsync(cancellationToken);
                        if (count != 0)
                        {
                            settings = new Settings() { Progress = null };
                            Logs.LogWarning("Corruption detected, reindexing the table...");
                            await Reindex(ctx, cancellationToken);
                            goto retry;
                        }
                        await SettingsRepository.UpdateSetting<Settings>(new Settings() { Complete = true }, SettingsKey);
                        Logs.LogInformation("Migration completed");
                        await PostMigrationCleanup(ctx, cancellationToken);
                        return;
                    }

                    try
                    {
                        progress = ProcessEntities(ctx, entities);
                        await ctx.SaveChangesAsync();
                        batchSize = BatchSize;
                    }
                    catch (Exception ex) when (ex is DbUpdateConcurrencyException or TimeoutException or OperationCanceledException)
                    {
                        batchSize /= 2;
                        batchSize = Math.Max(1, batchSize);
                        goto retry;
                    }
                }

                settings = new Settings() { Progress = progress };
                await SettingsRepository.UpdateSetting<Settings>(settings, SettingsKey);
            }
            catch (Exception ex)
            {
                Logs.LogError(ex, "Error while migrating");
                goto retry;
            }
        }
    }

    protected abstract Task PostMigrationCleanup(ApplicationDbContext ctx, CancellationToken cancellationToken);

    protected abstract Task Reindex(ApplicationDbContext ctx, CancellationToken cancellationToken);
    protected abstract IQueryable<TEntity> GetQuery(ApplicationDbContext ctx, DateTimeOffset? progress);
    protected abstract DateTimeOffset ProcessEntities(ApplicationDbContext ctx, List<TEntity> entities);
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
        _Cts?.Cancel();
        return _Migrating ?? Task.CompletedTask;
    }
}
