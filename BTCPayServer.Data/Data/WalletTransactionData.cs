using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class WalletTransactionData
    {
        public string WalletDataId { get; set; }
        public WalletData WalletData { get; set; }
        public string TransactionId { get; set; }
        [Obsolete]
        public string Labels { get; set; }
        public byte[] Blob { get; set; }

        public List<WalletLabelData> WalletLabels { get; set; }
        public List<WalletScriptData> WalletScripts { get; set; }

        internal static void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<WalletTransactionData>()
                .HasKey(o => new
                {
                    o.WalletDataId,
#pragma warning disable CS0618
                    o.TransactionId
#pragma warning restore CS0618
                });
            builder.Entity<WalletTransactionData>()
                .HasOne(o => o.WalletData)
                .WithMany(w => w.WalletTransactions).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
