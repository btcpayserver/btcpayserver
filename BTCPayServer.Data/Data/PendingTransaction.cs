using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

public class PendingTransaction: IHasBlob<PendingTransactionBlob>
    {
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

            builder.Entity<PendingTransaction>().HasKey(transaction => new {transaction.CryptoCode, transaction.TransactionId});

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
        public List<CollectedSignature> CollectedSignatures { get; set; } = new();
    }

    public class CollectedSignature
    {
        public DateTimeOffset Timestamp { get; set; }
        public string ReceivedPSBT { get; set; }
    }
