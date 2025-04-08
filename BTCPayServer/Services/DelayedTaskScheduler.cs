#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Amazon.Runtime.Internal.Util;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Services
{
    public class DelayedTaskScheduler : IDisposable
    {
        public DelayedTaskScheduler(ILogger<DelayedTaskScheduler> logger)
        {
            Logger = logger;
        }
        record class TimerState(string Key, Func<Task> Act);
        private readonly Dictionary<string, Timer> _timers = new();
        bool disposed = false;

        public ILogger<DelayedTaskScheduler> Logger { get; }

        public void Schedule(string key, DateTimeOffset executeAt, Func<Task> act)
        {
            lock (_timers)
            {
                if (disposed)
                    return;
                _timers.TryGetValue(key, out var existing);
                if (existing != null)
                {
                    existing.Dispose();
                    _timers.Remove(key);
                }
                var due = executeAt - DateTimeOffset.UtcNow;
                if (due < TimeSpan.Zero)
                    due = TimeSpan.Zero;
                var timer = new Timer(TimerCallback, new TimerState(key, act), Timeout.Infinite, Timeout.Infinite);
                _timers.Add(key, timer);
                timer.Change((long)due.TotalMilliseconds, (long)Timeout.Infinite);
            }
        }

        void TimerCallback(object? state)
        {
            var s = (TimerState)state!;
            Task.Run(async () =>
            {
                try
                {
                    await s.Act();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Error executing delayed task for key {s.Key}");
                }
                finally
                {
                    Timer? timer = null;
                    lock (_timers)
                    {
                        if (_timers.TryGetValue(s.Key, out timer))
                        {
                            _timers.Remove(s.Key);
                        }
                    }
                    timer?.Dispose();
                }
            });
        }

        public void Dispose()
        {
            lock (_timers)
            {
                disposed = true;
                foreach (var t in _timers.Values)
                    t.Dispose();
                _timers.Clear();
            }
        }
        }
}
