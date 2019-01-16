using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.Extensions.Hosting;
using NicolasDorier.RateLimits;

namespace BTCPayServer.HostedServices
{
    public class BackgroundJobSchedulerHostedService : IHostedService
    {
        public BackgroundJobSchedulerHostedService(IBackgroundJobClient backgroundJobClient)
        {
            BackgroundJobClient = (BackgroundJobClient)backgroundJobClient;
        }

        public BackgroundJobClient BackgroundJobClient { get; }

        Task _Loop;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Stop = new CancellationTokenSource();
            _Loop = BackgroundJobClient.ProcessJobs(_Stop.Token);
            return Task.CompletedTask;
        }

        CancellationTokenSource _Stop;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _Stop.Cancel();
            try
            {
                await _Loop;
            }
            catch (OperationCanceledException)
            {

            }
            await BackgroundJobClient.WaitAllRunning(cancellationToken);
        }
    }

    public class BackgroundJobClient : IBackgroundJobClient
    {
        class BackgroundJob
        {
            public Func<Task> Action;
            public TimeSpan Delay;
            public IDelay DelayImplementation;
            public BackgroundJob(Func<Task> action, TimeSpan delay, IDelay delayImplementation)
            {
                this.Action = action;
                this.Delay = delay;
                this.DelayImplementation = delayImplementation;
            }

            public async Task Run(CancellationToken cancellationToken)
            {
                await DelayImplementation.Wait(Delay, cancellationToken);
                await Action();
            }
        }

        public IDelay Delay { get; set; } = TaskDelay.Instance;
        public int GetExecutingCount()
        {
            lock (_Processing)
            {
                return _Processing.Count();
            }
        }

        private Channel<BackgroundJob> _Jobs = Channel.CreateUnbounded<BackgroundJob>();
        HashSet<Task> _Processing = new HashSet<Task>();
        public void Schedule(Func<Task> action, TimeSpan delay)
        {
            _Jobs.Writer.TryWrite(new BackgroundJob(action, delay, Delay));
        }

        public async Task WaitAllRunning(CancellationToken cancellationToken)
        {
            Task[] processing = null;
            lock (_Processing)
            {
                processing = _Processing.ToArray();
            }

            try
            {
                await Task.WhenAll(processing).WithCancellation(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        public async Task ProcessJobs(CancellationToken cancellationToken)
        {
            while (await _Jobs.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_Jobs.Reader.TryRead(out var job))
                {
                    var processing = job.Run(cancellationToken);
                    lock (_Processing)
                    {
                        _Processing.Add(processing);
                    }
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    processing.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            Logs.PayServer.LogWarning(t.Exception, "Unhandled exception while job running");
                        }
                        lock (_Processing)
                        {
                            _Processing.Remove(processing);
                        }
                    }, default, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                }
            }
        }
    }
}
