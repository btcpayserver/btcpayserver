using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;

namespace BTCPayServer.Services;

public class LightningHistogramService
{
    public Task<HistogramData> GetHistogram(ILightningClient lightningClient, HistogramType type, CancellationToken cancellationToken)
    {
        var (days, pointCount) = type switch
        {
            HistogramType.Day => (1, 30),
            HistogramType.Week => (7, 30),
            HistogramType.Month => (30, 30),
            HistogramType.YTD => (DateTimeOffset.Now.DayOfYear - 1, 30),
            HistogramType.Year => (365, 30),
            HistogramType.TwoYears => (730, 30),
            _ => throw new ArgumentException($"HistogramType {type} does not exist.")
        };
        var to = DateTimeOffset.UtcNow;
        var from = to - TimeSpan.FromDays(days);
        var ticks = (to - from).Ticks;

        // TODO: We can't just list all invoices and payments, we need to filter them by date
        // but the client doesn't support that yet so let's just disable this for now. See #6518
        return Task.FromResult<HistogramData>(null);
    }

    private class LnTx
    {
        public DateTimeOffset Settled { get; set; }
        public decimal Amount { get; set; }
    }
}
