using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using Xunit;

namespace BTCPayServer.Tests
{
    public class EventHostedServiceTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("Fast", "Fast")]
        public async Task CanCancelBarrierWhileEventIsProcessing(bool cancelBeforeWaiting)
        {
            var logs = new Logs();
            using var events = new EventAggregator(logs);
            var service = new BlockingEventService(events, logs);
            await service.StartAsync(CancellationToken.None);
            try
            {
                service.Enqueue();
                await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using var cts = new CancellationTokenSource();
                if (cancelBeforeWaiting)
                    cts.Cancel();
                var cancelledBarrier = service.WaitForPendingEventsAsync(cts.Token);
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => cancelledBarrier.WaitAsync(TimeSpan.FromSeconds(5)));

                // Cancelling one wait must not release the handler or cancel later barriers.
                var pendingBarrier = service.WaitForPendingEventsAsync();
                Assert.False(pendingBarrier.IsCompleted);
                service.Release.TrySetResult();
                await pendingBarrier.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(1, service.ProcessedEvents);
            }
            finally
            {
                service.Release.TrySetResult();
                await service.StopAsync(CancellationToken.None);
            }
        }

        private sealed class BlockingEventService(EventAggregator events, Logs logs)
            : EventHostedServiceBase(events, logs)
        {
            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int ProcessedEvents { get; private set; }

            public void Enqueue() => PushEvent(new object());

            protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
            {
                Started.TrySetResult();
                await Release.Task;
                ProcessedEvents++;
            }
        }
    }
}
