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
    public async Task<HistogramData> GetHistogram(ILightningClient lightningClient, HistogramType type, CancellationToken cancellationToken)
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
        var interval = TimeSpan.FromTicks(ticks / pointCount);

        try
        {
            // general balance
            var lnBalance = await lightningClient.GetBalance(cancellationToken);
            var total = lnBalance.OffchainBalance.Local;
            var totalBtc = total.ToDecimal(LightMoneyUnit.BTC);
            // prepare transaction data
            var lnInvoices = await lightningClient.ListInvoices(cancellationToken);
            var lnPayments = await lightningClient.ListPayments(cancellationToken);
            var lnTransactions = lnInvoices
                .Where(inv => inv.Status == LightningInvoiceStatus.Paid && inv.PaidAt >= from)
                .Select(inv => new LnTx { Amount = inv.Amount.ToDecimal(LightMoneyUnit.BTC), Settled = inv.PaidAt.GetValueOrDefault() })
                .Concat(lnPayments
                    .Where(pay => pay.Status == LightningPaymentStatus.Complete && pay.CreatedAt >= from)
                    .Select(pay => new LnTx { Amount = pay.Amount.ToDecimal(LightMoneyUnit.BTC) * -1, Settled = pay.CreatedAt.GetValueOrDefault() }))
                .OrderByDescending(tx => tx.Settled)
                .ToList();
            // assemble graph data going backwards
            var series = new List<decimal>(pointCount);
            var labels = new List<DateTimeOffset>(pointCount);
            var balance = totalBtc;
            for (var i = pointCount; i > 0; i--)
            {
                var txs = lnTransactions.Where(t =>
                    t.Settled.Ticks >= from.Ticks + interval.Ticks * i &&
                    t.Settled.Ticks < from.Ticks + interval.Ticks * (i + 1));
                var sum = txs.Sum(tx => tx.Amount);
                balance -= sum;
                series.Add(balance);
                labels.Add(from + interval * (i - 1));
            }
            // reverse the lists
            series.Reverse();
            labels.Reverse();
            return new HistogramData
            {
                Type = type,
                Balance = totalBtc,
                Series = series,
                Labels = labels
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private class LnTx
    {
        public DateTimeOffset Settled { get; set; }
        public decimal Amount { get; set; }
    }
}
