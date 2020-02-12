using System;
using NBitcoin;
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
            if (_Stop == null)
                return;
            _Stop.Cancel();
            try
            {
                await _Loop;
            }
            catch (OperationCanceledException)
            {

            }
            try
            {
                await BackgroundJobClient.WaitAllRunning(cancellationToken);
            }
            catch (OperationCanceledException)
            {

            }
        }
    }

    public class BackgroundJobClient : IBackgroundJobClient
    {
        class BackgroundJob
        {
            public Func<CancellationToken, Task> Action;
            public TimeSpan Delay;
            public IDelay DelayImplementation;
            public BackgroundJob(Func<CancellationToken, Task> action, TimeSpan delay, IDelay delayImplementation)
            {
                this.Action = action;
                this.Delay = delay;
                this.DelayImplementation = delayImplementation;
            }

            public async Task Run(CancellationToken cancellationToken)
            {
                await DelayImplementation.Wait(Delay, cancellationToken);
                await Action(cancellationToken);
            }
        }

        public IDelay Delay { get; set; } = TaskDelay.Instance;
        public int GetExecutingCount()
        {
            lock (_Processing)
            {
                return _Processing.Count;
            }
        }

        private Channel<BackgroundJob> _Jobs = Channel.CreateUnbounded<BackgroundJob>();
        HashSet<Task> _Processing = new HashSet<Task>();
        public void Schedule(Func<CancellationToken, Task> act, TimeSpan scheduledIn)
        {
            _Jobs.Writer.TryWrite(new BackgroundJob(act, scheduledIn, Delay));
        }

        public async Task WaitAllRunning(CancellationToken cancellationToken)
        {
            Task[] processing = null;
            lock (_Processing)
            {
                if (_Processing.Count == 0)
                    return;
                processing = _Processing.ToArray();
            }

            try
            {
                await Task.WhenAll(processing).WithCancellation(cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
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
                    _ = processing.ContinueWith(t =>
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
                }
            }
        }
    }
}
