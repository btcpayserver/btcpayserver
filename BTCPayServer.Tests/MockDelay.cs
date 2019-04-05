using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Tests
{
    public class MockDelay : IDelay
    {
        class WaitObj
        {
            public DateTimeOffset Expiration;
            public TaskCompletionSource<bool> CTS;
        }

        List<WaitObj> waits = new List<WaitObj>();
        DateTimeOffset _Now = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public async Task Wait(TimeSpan delay, CancellationToken cancellation)
        {
            WaitObj w = new WaitObj();
            w.Expiration = _Now + delay;
            w.CTS = new TaskCompletionSource<bool>();
            using (cancellation.Register(() =>
             {
                 w.CTS.TrySetCanceled();
             }))
            {
                lock (waits)
                {
                    waits.Add(w);
                }
                await w.CTS.Task;
            }
        }

        public async Task Advance(TimeSpan time)
        {
            _Now += time;
            List<WaitObj> overdue = new List<WaitObj>();
            lock (waits)
            {
                foreach (var wait in waits.ToArray())
                {
                    if (_Now >= wait.Expiration)
                    {
                        overdue.Add(wait);
                        waits.Remove(wait);
                    }
                }
            }
            foreach (var o in overdue)
                o.CTS.TrySetResult(true);
            try
            {
                await Task.WhenAll(overdue.Select(o => o.CTS.Task).ToArray());
            }
            catch { }
        }
        public override string ToString()
        {
            return _Now.Millisecond.ToString(CultureInfo.InvariantCulture);
        }
    }
}
