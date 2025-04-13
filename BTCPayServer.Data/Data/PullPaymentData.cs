using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NBitcoin;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        public string Currency { get; set; }
        public decimal Limit { get; set; }
        public DateTimeOffset StartDate { get; set; }
        public DateTimeOffset? EndDate { get; set; }
        public bool Archived { get; set; }
        public List<PayoutData> Payouts { get; set; }
        public string Blob { get; set; }


        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<PullPaymentData>()
                .HasIndex(o => o.StoreId);
            builder.Entity<PullPaymentData>()
            .HasOne(o => o.StoreData)
                .WithMany(o => o.PullPayments).OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PullPaymentData>()
                .Property(o => o.Blob)
                .HasColumnType("JSONB");
        }

        public (DateTimeOffset Start, DateTimeOffset? End)? GetPeriod(DateTimeOffset now)
        {
            if (now < StartDate)
                return null;
            if (EndDate is DateTimeOffset end && now >= end)
                return null;
            return (StartDate, EndDate);
        }

        public bool HasStarted()
        {
            return HasStarted(DateTimeOffset.UtcNow);
        }
        public TimeSpan? EndsIn() => EndsIn(DateTimeOffset.UtcNow);
        public TimeSpan? EndsIn(DateTimeOffset now)
        {
            if (EndDate is DateTimeOffset e)
            {
                var resetIn = (e - now);
                if (resetIn < TimeSpan.Zero)
                    resetIn = TimeSpan.Zero;
                return resetIn;
            }
            return null;
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
