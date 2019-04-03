using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Logging
{
    public class InvoiceLog
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Log { get; set; }

        public override string ToString()
        {
            return $"{Timestamp.UtcDateTime}: {Log}";
        }
    }
    public class InvoiceLogs
    {
        List<InvoiceLog> _InvoiceLogs = new List<InvoiceLog>();
        public void Write(string data)
        {
            lock (_InvoiceLogs)
            {
                _InvoiceLogs.Add(new InvoiceLog() { Timestamp = DateTimeOffset.UtcNow, Log = data });
            }
        }

        public List<InvoiceLog> ToList()
        {
            lock (_InvoiceLogs)
            {
                return _InvoiceLogs.ToList();
            }
        }

        internal IDisposable Measure(string logs)
        {
            return new Mesuring(this, logs);
        }

        class Mesuring : IDisposable
        {
            private readonly InvoiceLogs _logs;
            private readonly string _msg;
            private readonly DateTimeOffset _Before;
            public Mesuring(InvoiceLogs logs, string msg)
            {
                _logs = logs;
                _msg = msg;
                _Before = DateTimeOffset.UtcNow;
            }
            public void Dispose()
            {
                var timespan = DateTimeOffset.UtcNow - _Before;
                if (timespan.TotalSeconds >= 1.0)
                {
                    _logs.Write($"{_msg} took {(int)timespan.TotalSeconds} seconds");
                }
                else
                {
                    _logs.Write($"{_msg} took {(int)timespan.TotalMilliseconds} milliseconds");
                }
            }
        }
    }
}
