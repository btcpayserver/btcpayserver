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
        /// <summary>
        /// For example, BTC-CHAIN needs to ensure that only a single address is tied to an active payout.
        /// If `PayoutBlob.Destination` is `bitcoin://1BvBMSeYstWetqTFn5Au4m4GFg7xJaNVN2?amount=0.1`
        /// Then `DedupId` is `1BvBMSeYstWetqTFn5Au4m4GFg7xJaNVN2`
        /// For Lightning, Destination could be the lightning address, BOLT11 or LNURL
        /// But the `DedupId` would be the `PaymentHash`.
        /// </summary>
        public string? DedupId { get; set; }
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
                .HasIndex(x => new { DestinationId = x.DedupId, x.State });

            builder.Entity<PayoutData>()
                .Property(o => o.Blob)
                .HasColumnType("JSONB");
            builder.Entity<PayoutData>()
                .Property(o => o.Proof)
                .HasColumnType("JSONB");
        }
    }
}
