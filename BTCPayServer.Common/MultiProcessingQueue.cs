using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ProcessingAction = System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task>;

namespace BTCPayServer
{
    /// <summary>
    /// This class make sure that enqueued actions sharing the same queue name
    /// are executed sequentially.
    /// This is useful to preserve order of events.
    /// </summary>
    public class MultiProcessingQueue
    {
        Dictionary<string, ProcessingQueue> _Queues = new Dictionary<string, ProcessingQueue>();
        class ProcessingQueue
        {
            internal Channel<ProcessingAction> Chan = Channel.CreateUnbounded<ProcessingAction>();
            internal Task ProcessTask;
            public async Task Process(CancellationToken cancellationToken)
            {
retry:
                while (Chan.Reader.TryRead(out var item))
                {
                    await item(cancellationToken);
                }
                if (Chan.Writer.TryComplete())
                {
                    goto retry;
                }
            }
        }

        public int QueueCount
        {
            get
            {
                lock (_Queues)
                {
                    Cleanup();
                    return _Queues.Count;
                }
            }
        }
        CancellationTokenSource cts = new CancellationTokenSource();
        bool stopped;
        public void Enqueue(string queueName, ProcessingAction act)
        {
            lock (_Queues)
            {
retry:
                if (stopped)
                    return;
                Cleanup();
                bool created = false;
                if (!_Queues.TryGetValue(queueName, out var queue))
                {
                    queue = new ProcessingQueue();
                    _Queues.Add(queueName, queue);
                    created = true;
                }
                if (!queue.Chan.Writer.TryWrite(act))
                    goto retry;
                if (created)
                    queue.ProcessTask = queue.Process(cts.Token);
            }
        }

        private void Cleanup()
        {
            var removeList = new List<string>();
            foreach (var q in _Queues)
            {
                if (q.Value.Chan.Reader.Completion.IsCompletedSuccessfully)
                {
                    removeList.Add(q.Key);
                }
            }
            foreach (var q in removeList)
            {
                _Queues.Remove(q);
            }
        }

        public async Task Abort(CancellationToken cancellationToken)
        {
            stopped = true;
            ProcessingQueue[] queues = null;
            lock (_Queues)
            {
                queues = _Queues.Select(c => c.Value).ToArray();
            }
            cts.Cancel();
            var delay = Task.Delay(-1, cancellationToken);
            foreach (var q in queues)
            {
                try
                {
                    await Task.WhenAny(q.ProcessTask, delay);
                }
                catch
                {
                }
            }
            lock (_Queues)
            {
                if (_Queues.Count is 0)
                    return;
                cancellationToken.ThrowIfCancellationRequested();
                Cleanup();
            }
        }
    }
}
