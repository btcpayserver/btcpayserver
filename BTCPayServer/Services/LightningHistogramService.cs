using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Services.Wallets;

namespace BTCPayServer.Services;

public class LightningHistogramService
{
    public async Task<WalletHistogramData> GetHistogram(ILightningClient lightningClient, WalletHistogramType type, CancellationToken cancellationToken)
    {
        var (days, pointCount) = type switch
        {
            WalletHistogramType.Week => (7, 30),
            WalletHistogramType.Month => (30, 30),
            WalletHistogramType.Year => (365, 30),
            _ => throw new ArgumentException($"WalletHistogramType {type} does not exist.")
        };
        var to = DateTimeOffset.UtcNow;
        var from = to - TimeSpan.FromDays(days);
        var ticks = (to - from).Ticks;
        var interval = TimeSpan.FromTicks(ticks / pointCount);

        try
        {
            // general balance
            var lnBalance = await lightningClient.GetBalance(cancellationToken);
            /*var totalOnchain = lnBalance.OnchainBalance != null
                ? (lnBalance.OnchainBalance.Confirmed ?? 0L) + (lnBalance.OnchainBalance.Reserved ?? 0L) +
                  (lnBalance.OnchainBalance.Unconfirmed ?? 0L)
                : new Money(0L);
            var totalOffchain = lnBalance.OffchainBalance != null
                ? (lnBalance.OffchainBalance.Opening ?? 0) + (lnBalance.OffchainBalance.Local ?? 0) +
                  (lnBalance.OffchainBalance.Closing ?? 0)
                : null;*/
            var total = lnBalance.OffchainBalance.Local;//(totalOnchain + new Money(totalOffchain?.ToDecimal(LightMoneyUnit.Satoshi) ?? 0, MoneyUnit.Satoshi)).ToDecimal(MoneyUnit.Satoshi);
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
                labels.Add(from + interval * i);
            }
            // reverse the lists
            series.Reverse();
            labels.Reverse();
            return new WalletHistogramData
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
