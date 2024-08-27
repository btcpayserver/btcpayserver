using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using BTCPayServer.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NBitcoin;

namespace BTCPayServer.Data
{
    public partial class PayoutData
    {
        [Key]
        [MaxLength(30)]
        public string Id { get; set; }
        public DateTimeOffset Date { get; set; }
        public string PullPaymentDataId { get; set; }
        public string StoreDataId { get; set; }
        /// <summary>
        /// The currency of the payout (eg. BTC)
        /// </summary>
        public string Currency { get; set; }
        /// <summary>
        /// The amount of the payout in Currency.
        /// The Amount only get set when the payout is actually approved.
        /// </summary>
        public decimal? Amount { get; set; }
        /// <summary>
        /// The original currency of the payout (eg. USD)
        /// </summary>
        public string OriginalCurrency { get; set; }
        /// <summary>
        /// The amount of the payout in OriginalCurrency
        /// </summary>
        public decimal OriginalAmount { get; set; }
        public PullPaymentData PullPaymentData { get; set; }
        [MaxLength(20)]
        public PayoutState State { get; set; }
        [MaxLength(20)]
        [Required]
        public string PayoutMethodId { get; set; }
        public string Blob { get; set; }
        public string Proof { get; set; }
#nullable enable
        public string? Destination { get; set; }
#nullable restore
        public StoreData StoreData { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<PayoutData>()
                .HasOne(o => o.PullPaymentData)
                .WithMany(o => o.Payouts).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PayoutData>()
                .HasOne(o => o.StoreData)
                .WithMany(o => o.Payouts).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PayoutData>()
                .Property(o => o.State)
                .HasConversion<string>();
            builder.Entity<PayoutData>()
                .HasIndex(o => o.State);
            builder.Entity<PayoutData>()
                .HasIndex(x => new { DestinationId = x.Destination, x.State });

            builder.Entity<PayoutData>()
                .Property(o => o.Blob)
                .HasColumnType("JSONB");
            builder.Entity<PayoutData>()
                .Property(o => o.Proof)
                .HasColumnType("JSONB");
        }
    }
}
