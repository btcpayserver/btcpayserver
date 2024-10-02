using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.PayoutProcessors;

public class PayoutProcessorUpdated
{
    public string Id { get; set; }
    public PayoutProcessorData Data { get; set; }

    public TaskCompletionSource Processed { get; set; }
    public override string ToString()
    {
        return $"{Data}";
    }
}

public class PayoutProcessorService : EventHostedServiceBase
{
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly IEnumerable<IPayoutProcessorFactory> _payoutProcessorFactories;


    private ConcurrentDictionary<string, IHostedService> Services { get; set; } = new();
    public PayoutProcessorService(
        ApplicationDbContextFactory applicationDbContextFactory,
        EventAggregator eventAggregator,
        Logs logs,
        IEnumerable<IPayoutProcessorFactory> payoutProcessorFactories) : base(eventAggregator, logs)
    {
        _applicationDbContextFactory = applicationDbContextFactory;
        _payoutProcessorFactories = payoutProcessorFactories;
    }

    public class PayoutProcessorQuery
    {
        public PayoutProcessorQuery()
        {
            
        }
        public PayoutProcessorQuery(string storeId, PayoutMethodId payoutMethodId)
        {
            Stores = new[] { storeId };
            PayoutMethods = new[] { payoutMethodId };
        }
        public string[] Stores { get; set; }
        public string[] Processors { get; set; }
        public PayoutMethodId[] PayoutMethods { get; set; }
    }

    public async Task<List<PayoutProcessorData>> GetProcessors(PayoutProcessorQuery query)
    {

        await using var context = _applicationDbContextFactory.CreateContext();
        var queryable = context.PayoutProcessors.AsQueryable();
        if (query.Processors is not null)
        {
            queryable = queryable.Where(data => query.Processors.Contains(data.Processor));
        }
        if (query.Stores is not null)
        {
            queryable = queryable.Where(data => query.Stores.Contains(data.StoreId));
        }
        if (query.PayoutMethods is not null)
        {
            var paymentMethods = query.PayoutMethods.Select(d => d.ToString()).Distinct().ToArray();
            queryable = queryable.Where(data => paymentMethods.Contains(data.PayoutMethodId));
        }

        return await queryable.ToListAsync();
    }

    private async Task RemoveProcessor(string id)
    {
        await using var context = _applicationDbContextFactory.CreateContext();
        var item = await context.FindAsync<PayoutProcessorData>(id);
        if (item is not null)
            context.Remove(item);
        await context.SaveChangesAsync();
        await StopProcessor(id, CancellationToken.None);
    }

    private async Task AddOrUpdateProcessor(PayoutProcessorData data)
    {

        await using var context = _applicationDbContextFactory.CreateContext();
        if (string.IsNullOrEmpty(data.Id))
        {
            await context.AddAsync(data);
        }
        else
        {
            context.Update(data);
        }
        await context.SaveChangesAsync();
        await StartOrUpdateProcessor(data, CancellationToken.None);
    }

    protected override void SubscribeToEvents()
    {
        base.SubscribeToEvents();
        Subscribe<PayoutProcessorUpdated>();
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken);
        var activeProcessors = await GetProcessors(new PayoutProcessorQuery());
        var tasks = activeProcessors.Select(data => StartOrUpdateProcessor(data, cancellationToken));
        await Task.WhenAll(tasks);
    }

    private async Task StopProcessor(string id, CancellationToken cancellationToken)
    {
        if (Services.Remove(id, out var currentService))
        {
            await currentService.StopAsync(cancellationToken);
        }

    }

    private async Task StartOrUpdateProcessor(PayoutProcessorData data, CancellationToken cancellationToken)
    {
        var matchedProcessor = _payoutProcessorFactories.FirstOrDefault(factory =>
            factory.Processor == data.Processor);

        if (matchedProcessor is not null)
        {
            await StopProcessor(data.Id, cancellationToken);
            IHostedService processor = null;
            try
            {
                processor = await matchedProcessor.ConstructProcessor(data);
            }
            catch(Exception ex)
            {
                Logs.PayServer.LogWarning(ex, $"Payout processor ({data.PayoutMethodId}) failed to start. Skipping...");
                return;
            }
            await processor.StartAsync(cancellationToken);
            Services.TryAdd(data.Id, processor);
        }

    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await StopAllService(cancellationToken);
    }

    private async Task StopAllService(CancellationToken cancellationToken)
    {
        foreach (KeyValuePair<string, IHostedService> service in Services)
        {
            await service.Value.StopAsync(cancellationToken);
        }
        Services.Clear();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        await base.ProcessEvent(evt, cancellationToken);

        if (evt is PayoutProcessorUpdated processorUpdated)
        {
            if (processorUpdated.Data is null)
            {
                await RemoveProcessor(processorUpdated.Id);
            }
            else
            {
                await AddOrUpdateProcessor(processorUpdated.Data);
            }

            processorUpdated.Processed?.SetResult();
        }
    }

    internal async Task Restart(PayoutProcessorQuery payoutProcessorQuery)
    {
        foreach (var data in await GetProcessors(payoutProcessorQuery))
        {
            await StartOrUpdateProcessor(data, default);
        }
    }
}
