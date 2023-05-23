using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices
{
    public abstract class BaseAsyncService : IHostedService
    {
        private CancellationTokenSource _Cts = new CancellationTokenSource();
        protected Task[] _Tasks;
        public readonly Logs Logs;

        public bool NoLogsOnExit { get; set; }

        protected BaseAsyncService(Logs logs)
        {
            Logs = logs;
        }

        protected BaseAsyncService(ILogger logger)
        {
            Logs = new Logs() { PayServer = logger, Events = logger, Configuration = logger };
        }

        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            _Tasks = InitializeTasks();
            foreach (var t in _Tasks)
                t.ContinueWith(t =>
                {
                    if (t.IsFaulted && !CancellationToken.IsCancellationRequested)
                        Logs.PayServer.LogWarning(t.Exception, $"Unhanded exception in {this.GetType().Name}");
                }, TaskScheduler.Default);
            return Task.CompletedTask;
        }

        internal abstract Task[] InitializeTasks();

        protected CancellationToken CancellationToken
        {
            get { return _Cts.Token; }
        }

        protected async Task CreateLoopTask(Func<Task> act, [CallerMemberName] string caller = null)
        {
            await new SynchronizationContextRemover();
            while (!_Cts.IsCancellationRequested)
            {
                try
                {
                    await act();
                }
                catch (OperationCanceledException) when (_Cts.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogWarning(ex, caller + " failed");
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), _Cts.Token);
                    }
                    catch (OperationCanceledException) when (_Cts.IsCancellationRequested) { }
                }
            }
        }

        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_Cts != null)
            {
                _Cts.Cancel();
                if (_Tasks != null)
                    await Task.WhenAll(_Tasks);
            }
            if (!NoLogsOnExit)
                Logs.PayServer.LogInformation($"{this.GetType().Name} successfully exited...");
        }
    }
}
