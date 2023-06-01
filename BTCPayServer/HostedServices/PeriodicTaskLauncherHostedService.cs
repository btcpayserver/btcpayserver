using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BTCPayServer.HostedServices
{
    public class PeriodicTaskLauncherHostedService : IHostedService
    {
        public PeriodicTaskLauncherHostedService(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
        {
            ServiceProvider = serviceProvider;
            Logger = loggerFactory.CreateLogger("BTCPayServer.PeriodicTasks");
        }

        public IServiceProvider ServiceProvider { get; }
        public ILogger Logger { get; }

        Channel<ScheduledTask> jobs = Channel.CreateBounded<ScheduledTask>(100);
        CancellationTokenSource cts;
        public Task StartAsync(CancellationToken cancellationToken)
        {
            cts = new CancellationTokenSource();
            foreach (var task in ServiceProvider.GetServices<ScheduledTask>())
                jobs.Writer.TryWrite(task);

            loop = Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Loop(cts.Token)).ToArray());
            return Task.CompletedTask;
        }
        Task loop;
        private async Task Loop(CancellationToken token)
        {
            try
            {
                await foreach (var job in jobs.Reader.ReadAllAsync(token))
                {
                    if (job.NextScheduled <= DateTimeOffset.UtcNow)
                    {
                        var t = (IPeriodicTask)ServiceProvider.GetService(job.PeriodicTaskType);
                        try
                        {
                            await t.Do(token);
                        }
                        catch when (token.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, $"Unhandled error in periodic task {job.PeriodicTaskType.Name}");
                        }
                        finally
                        {
                            job.NextScheduled = DateTimeOffset.UtcNow + job.Every;
                        }
                    }
                    _ = Wait(job, token);
                }
            }
            catch when (token.IsCancellationRequested)
            {
            }
        }

        private async Task Wait(ScheduledTask job, CancellationToken token)
        {
            var timeToWait = job.NextScheduled - DateTimeOffset.UtcNow;
            try
            {
                await Task.Delay(timeToWait, token);
            }
            catch { }
            while (await jobs.Writer.WaitToWriteAsync())
            {
                if (jobs.Writer.TryWrite(job))
                    break;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            cts?.Cancel();
            jobs.Writer.TryComplete();
            if (loop is not null)
                await loop;
        }
    }
}
