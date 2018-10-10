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
    }
}
