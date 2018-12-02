using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer
{
    class CustomThreadPool : IDisposable
    {
        CancellationTokenSource _Cancel = new CancellationTokenSource();
        TaskCompletionSource<bool> _Exited;
        int _ExitedCount = 0;
        Thread[] _Threads;
        Exception _UnhandledException;
        BlockingCollection<(Action, TaskCompletionSource<object>)> _Actions = new BlockingCollection<(Action, TaskCompletionSource<object>)>(new ConcurrentQueue<(Action, TaskCompletionSource<object>)>());

        public CustomThreadPool(int threadCount, string threadName)
        {
            if (threadCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(threadCount));
            _Exited = new TaskCompletionSource<bool>();
            _Threads = Enumerable.Range(0, threadCount).Select(_ => new Thread(RunLoop) { Name = threadName }).ToArray();
            foreach (var t in _Threads)
                t.Start();
        }

        public void Do(Action act)
        {
            DoAsync(act).GetAwaiter().GetResult();
        }

        public T Do<T>(Func<T> act)
        {
            return DoAsync(act).GetAwaiter().GetResult();
        }

        public async Task<T> DoAsync<T>(Func<T> act)
        {
            TaskCompletionSource<object> done = new TaskCompletionSource<object>();
            _Actions.Add((() =>
            {
                try
                {
                    done.TrySetResult(act());
                }
                catch (Exception ex) { done.TrySetException(ex); }
            }
            , done));
            return (T)(await done.Task.ConfigureAwait(false));
        }

        public Task DoAsync(Action act)
        {
            return DoAsync<object>(() =>
            {
                act();
                return null;
            });
        }

        void RunLoop()
        {
            try
            {
                foreach (var act in _Actions.GetConsumingEnumerable(_Cancel.Token))
                {
                    act.Item1();
                }
            }
            catch (OperationCanceledException) when (_Cancel.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _Cancel.Cancel();
                _UnhandledException = ex;
            }
            if (Interlocked.Increment(ref _ExitedCount) == _Threads.Length)
            {
                foreach (var action in _Actions)
                {
                    try
                    {
                        action.Item2.TrySetCanceled();
                    }
                    catch { }
                }
                _Exited.TrySetResult(true);
            }
        }

        public void Dispose()
        {
            _Cancel.Cancel();
            _Exited.Task.GetAwaiter().GetResult();
        }
    }
}
