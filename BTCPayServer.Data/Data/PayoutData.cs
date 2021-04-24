using System;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Client.Models;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Data
{
    public class PayoutData
    {
        [Key]
        [MaxLength(30)]
        public string Id { get; set; }
        public DateTimeOffset Date { get; set; }
        public string PullPaymentDataId { get; set; }
        public PullPaymentData PullPaymentData { get; set; }
        [MaxLength(20)]
        public PayoutState State { get; set; }
        [MaxLength(20)]
        [Required]
        public string PaymentMethodId { get; set; }
        public string Destination { get; set; }
        public byte[] Blob { get; set; }
        public byte[] Proof { get; set; }


        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<PayoutData>()
                .HasOne(o => o.PullPaymentData)
                .WithMany(o => o.Payouts).OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PayoutData>()
                .Property(o => o.State)
                .HasConversion<string>();
            builder.Entity<PayoutData>()
                .HasIndex(o => o.Destination)
                .IsUnique();
            builder.Entity<PayoutData>()
                .HasIndex(o => o.State);
        }

        // utility methods
        public bool IsInPeriod(PullPaymentData pp, DateTimeOffset now)
        {
            var period = pp.GetPeriod(now);
            if (period is { } p)
            {
                return p.Start <= Date && (p.End is DateTimeOffset end ? Date < end : true);
            }
            else
            {
                return false;
            }
        }
    }
}
