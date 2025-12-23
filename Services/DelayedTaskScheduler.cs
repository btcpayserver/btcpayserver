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
        record class TimerState(string Key, Func<Task> Act, DateTimeOffset ExecuteAt)
        {
            internal TimeSpan NextWait()
            {
                var due = ExecuteAt - DateTimeOffset.UtcNow;
                if (due < TimeSpan.Zero)
                    due = TimeSpan.Zero;
                else
                    due += TimeSpan.FromSeconds(1.0); // Better to be a bit late than too early
                // Max timer needed, else dotnet crash
                if (due > MaxTimer)
                    due = MaxTimer;
                return due;
            }
        }

        private readonly Dictionary<string, Timer> _timers = new();
        bool disposed = false;

        public ILogger<DelayedTaskScheduler> Logger { get; }
        static TimeSpan MaxTimer = TimeSpan.FromMilliseconds(4294967294);
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

                var state = new TimerState(key, act, executeAt);
                var timer = new Timer(TimerCallback, state, Timeout.Infinite, Timeout.Infinite);
                _timers.Add(key, timer);
                timer.Change(state.NextWait(), Timeout.InfiniteTimeSpan);
            }
        }



        void TimerCallback(object? state)
        {
            var s = (TimerState)state!;
            Task.Run(async () =>
            {
                bool run = s.NextWait() < TimeSpan.FromSeconds(5.0);
                try
                {
                    if (run)
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
                            if (run)
                                _timers.Remove(s.Key);
                            else
                                timer.Change(s.NextWait(), Timeout.InfiniteTimeSpan);
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
