using System;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;

namespace BTCPayServer.HostedServices
{
    public class DelayedTransactionBroadcasterHostedService : BaseAsyncService
    {
        private readonly DelayedTransactionBroadcaster _transactionBroadcaster;

        public DelayedTransactionBroadcasterHostedService(DelayedTransactionBroadcaster transactionBroadcaster, Logs logs) : base(logs)
        {
            _transactionBroadcaster = transactionBroadcaster;
        }

        internal override Task[] InitializeTasks()
        {
            return new Task[]
            {
                CreateLoopTask(Rebroadcast)
            };
        }

        public TimeSpan PollInternal { get; set; } = TimeSpan.FromMinutes(1.0);

        async Task Rebroadcast()
        {
            while (true)
            {
                await _transactionBroadcaster.ProcessAll(CancellationToken);
                await Task.Delay(PollInternal, CancellationToken);
            }
        }
    }
}
