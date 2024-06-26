using System;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class InvoiceEventData
    {
        public string InvoiceDataId { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string Message { get; set; }
        public EventSeverity Severity { get; set; } = EventSeverity.Info;

        public enum EventSeverity
        {
            Info,
            Error,
            Success,
            Warning
        }

        public string GetCssClass()
        {
            return Severity switch
            {
                EventSeverity.Error => "danger",
                EventSeverity.Success => "success",
                EventSeverity.Warning => "warning",
                _ => null
            };
        }
    }
}
