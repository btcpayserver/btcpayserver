using System;
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

        public void Advance(TimeSpan time)
        {
            _Now += time;
            lock (waits)
            {
                foreach (var wait in waits.ToArray())
                {
                    if (_Now >= wait.Expiration)
                    {
                        wait.CTS.TrySetResult(true);
                        waits.Remove(wait);
                    }
                }
            }
        }

        public void AdvanceMilliseconds(long milli)
        {
            Advance(TimeSpan.FromMilliseconds(milli));
        }

        public override string ToString()
        {
            return _Now.Millisecond.ToString(CultureInfo.InvariantCulture);
        }
    }
}
