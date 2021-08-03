using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using BTCPayServer.Client.Models;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Data
{

    public class PullPaymentData
    {
        [Key]
        [MaxLength(30)]
        public string Id { get; set; }
        [ForeignKey("StoreId")]
        public StoreData StoreData { get; set; }
        [MaxLength(50)]
        public string StoreId { get; set; }
        public long? Period { get; set; }
        public DateTimeOffset StartDate { get; set; }
        public DateTimeOffset? EndDate { get; set; }
        public bool Archived { get; set; }
        public List<PayoutData> Payouts { get; set; }
        public byte[] Blob { get; set; }


        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<PullPaymentData>()
                .HasIndex(o => o.StoreId);
            builder.Entity<PullPaymentData>()
                .HasOne(o => o.StoreData)
                .WithMany(o => o.PullPayments).OnDelete(DeleteBehavior.Cascade);
        }

        public (DateTimeOffset Start, DateTimeOffset? End)? GetPeriod(DateTimeOffset now)
        {
            if (now < StartDate)
                return null;
            if (EndDate is DateTimeOffset end && now >= end)
                return null;
            DateTimeOffset startPeriod = StartDate;
            DateTimeOffset? endPeriod = null;
            if (Period is long periodSeconds)
            {
                var period = TimeSpan.FromSeconds(periodSeconds);
                var timeToNow = now - StartDate;
                var periodCount = (long)timeToNow.TotalSeconds / (long)period.TotalSeconds;
                startPeriod = StartDate + (period * periodCount);
                endPeriod = startPeriod + period;
            }
            if (EndDate is DateTimeOffset end2 &&
                ((endPeriod is null) ||
                (endPeriod is DateTimeOffset endP && endP > end2)))
                endPeriod = end2;
            return (startPeriod, endPeriod);
        }

        public bool HasStarted()
        {
            return HasStarted(DateTimeOffset.UtcNow);
        }
        public bool HasStarted(DateTimeOffset now)
        {
            return StartDate <= now;
        }

        public bool IsExpired()
        {
            return IsExpired(DateTimeOffset.UtcNow);
        }
        public bool IsExpired(DateTimeOffset now)
        {
            return EndDate is DateTimeOffset dt && now > dt;
        }

        public bool IsRunning()
        {
            return IsRunning(DateTimeOffset.UtcNow);
        }

        public bool IsRunning(DateTimeOffset now)
        {
            return !Archived && !IsExpired(now) && HasStarted(now);
        }
    }

    public static class PayoutExtensions
    {
        public static IQueryable<PayoutData> GetPayoutInPeriod(this IQueryable<PayoutData> payouts, PullPaymentData pp)
        {
            return GetPayoutInPeriod(payouts, pp, DateTimeOffset.UtcNow);
        }
        public static IQueryable<PayoutData> GetPayoutInPeriod(this IQueryable<PayoutData> payouts, PullPaymentData pp, DateTimeOffset now)
        {
            var request = payouts.Where(p => p.PullPaymentDataId == pp.Id);
            var period = pp.GetPeriod(now);
            if (period is { } p)
            {
                var start = p.Start;
                if (p.End is DateTimeOffset end)
                {
                    return request.Where(p => p.Date >= start && p.Date < end);
                }
                else
                {
                    return request.Where(p => p.Date >= start);
                }
            }
            else
            {
                return request.Where(p => false);
            }
        }

        public static string GetStateString(this PayoutState state)
        {
            switch (state)
            {
                case PayoutState.AwaitingApproval:
                    return "Awaiting Approval";
                case PayoutState.AwaitingPayment:
                    return "Awaiting Payment";
                case PayoutState.InProgress:
                    return "In Progress";
                case PayoutState.Completed:
                    return "Completed";
                case PayoutState.Cancelled:
                    return "Cancelled";
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }
    }
}
