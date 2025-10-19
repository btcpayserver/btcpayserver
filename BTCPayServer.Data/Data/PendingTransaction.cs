using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using BTCPayServer.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json;

namespace BTCPayServer.Data;

public class PendingTransaction: IHasBlob<PendingTransactionBlob>
    {
        public string Id { get; set; }
        public string TransactionId { get; set; }
        public string CryptoCode { get; set; }
        public string StoreId { get; set; }
        public StoreData Store { get; set; }
        public DateTimeOffset? Expiry { get; set; }
        public PendingTransactionState State { get; set; }
        public string[] OutpointsUsed { get; set; }

        [NotMapped][Obsolete("Use Blob2 instead")]
        public byte[] Blob { get; set; }

        public string Blob2 { get; set; }


        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<PendingTransaction>()
                .HasOne(o => o.Store)
                .WithMany(i => i.PendingTransactions)
                .HasForeignKey(i => i.StoreId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PendingTransaction>().HasKey(t => t.Id);
            builder.Entity<PendingTransaction>().HasIndex(t => new { t.StoreId });
            builder.Entity<PendingTransaction>().HasIndex(t => new { t.TransactionId });

            builder.Entity<PendingTransaction>()
                .Property(o => o.Blob2)
                .HasColumnType("JSONB");
            builder.Entity<PendingTransaction>()
                .Property(o => o.OutpointsUsed)
                .HasColumnType("text[]");
        }
    }
    public enum PendingTransactionState
    {
        Pending,
        Cancelled,
        Expired,
        Invalidated,
        Signed,
        Broadcast
    }

    public class PendingTransactionBlob
    {
        public string PSBT { get; set; }
        public string RequestBaseUrl { get; set; }
        public List<CollectedSignature> CollectedSignatures { get; set; } = new();

        public int? SignaturesCollected { get; set; }
        // for example: 3/5
        public int? SignaturesNeeded { get; set; }
        public int? SignaturesTotal { get; set; }
    }

    public class CollectedSignature
    {
        public DateTimeOffset Timestamp { get; set; }
        public string ReceivedPSBT { get; set; }
    }
