using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Hosting;
using BTCPayServer.Logging;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text;

namespace BTCPayServer.HostedServices
{
    public abstract class BaseAsyncService : IHostedService
    {
        private CancellationTokenSource _Cts;
        protected Task[] _Tasks;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = new CancellationTokenSource();
            _Tasks = InitializeTasks();
            return Task.CompletedTask;
        }

        internal abstract Task[] InitializeTasks();

        protected CancellationToken Cancellation
        {
            get { return _Cts.Token; }
        }

        protected async Task CreateLoopTask(Func<Task> act, [CallerMemberName]string caller = null)
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

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Cts.Cancel();
            return Task.WhenAll(_Tasks);
        }
    }
}
