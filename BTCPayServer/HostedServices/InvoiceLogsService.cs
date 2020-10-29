using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.HostedServices
{
    public class InvoiceLogsService : IHostedService
    {
        private readonly ApplicationDbContextFactory _applicationDbContextFactory;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private CancellationToken _cts;
        public ConcurrentQueue<InvoiceEventData> QueuedLogs { get; set; } = new ConcurrentQueue<InvoiceEventData>();

        public InvoiceLogsService(ApplicationDbContextFactory applicationDbContextFactory,
            IBackgroundJobClient backgroundJobClient)
        {
            _applicationDbContextFactory = applicationDbContextFactory;
            _backgroundJobClient = backgroundJobClient;
        }

        private async Task ProcessLogs(CancellationToken arg)
        {
            try
            {
                if (!QueuedLogs.IsEmpty)
                {
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(arg, _cts);
                    await using var context = _applicationDbContextFactory.CreateContext();
                    while (QueuedLogs.TryDequeue(out var log))
                    {
                        await context.InvoiceEvents.AddAsync(log, cts.Token);
                    }

                    await context.SaveChangesAsync(cts.Token);
                }
            }
            catch
            {
                // ignored
            }

            _backgroundJobClient.Schedule(ProcessLogs, TimeSpan.FromSeconds(5));
        }

        public void AddInvoiceLogs(string invoiceId, InvoiceLogs logs)
        {
            foreach (var log in logs.ToList())
            {
                QueuedLogs.Enqueue(new InvoiceEventData()
                {
                    Severity = log.Severity,
                    InvoiceDataId = invoiceId,
                    Message = log.Log,
                    Timestamp = log.Timestamp,
                    UniqueId = Encoders.Hex.EncodeData(RandomUtils.GetBytes(10))
                });
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = cancellationToken;
            _backgroundJobClient.Schedule(ProcessLogs, TimeSpan.Zero);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
