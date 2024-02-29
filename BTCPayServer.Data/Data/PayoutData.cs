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
    public class PayoutData
    {
        [Key]
        [MaxLength(30)]
        public string Id { get; set; }
        public DateTimeOffset Date { get; set; }
        public string PullPaymentDataId { get; set; }
        public string StoreDataId { get; set; }
        public PullPaymentData PullPaymentData { get; set; }
        [MaxLength(20)]
        public PayoutState State { get; set; }
        [MaxLength(20)]
        [Required]
        public string PaymentMethodId { get; set; }
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

            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<PayoutData>()
                    .Property(o => o.Blob)
                    .HasColumnType("JSONB");
                builder.Entity<PayoutData>()
                    .Property(o => o.Proof)
                    .HasColumnType("JSONB");
            }
            else if (databaseFacade.IsMySql())
            {
                builder.Entity<PayoutData>()
                    .Property(o => o.Blob)
                    .HasConversion(new ValueConverter<string, byte[]>
                    (
                        convertToProviderExpression: (str) => Encoding.UTF8.GetBytes(str),
                        convertFromProviderExpression: (bytes) => Encoding.UTF8.GetString(bytes)
                    ));
                builder.Entity<PayoutData>()
                    .Property(o => o.Proof)
                    .HasConversion(new ValueConverter<string, byte[]>
                    (
                        convertToProviderExpression: (str) => Encoding.UTF8.GetBytes(str),
                        convertFromProviderExpression: (bytes) => Encoding.UTF8.GetString(bytes)
                    ));
            }
        }

        // utility methods
        public bool IsInPeriod(PullPaymentData pp, DateTimeOffset now)
        {
            var period = pp.GetPeriod(now);
            if (period is { } p)
            {
                return p.Start <= Date && (p.End is not { } end || Date < end);
            }

            return false;
        }
    }
}
