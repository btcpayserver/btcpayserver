using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

public class WalletLabelData
{
    public string WalletDataId { get; set; }
    public string Label { get; set; }
    public string Data { get; set; }
    internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        builder.Entity<WalletLabelData>()
            .HasKey(o => new
            {
                o.WalletDataId,
#pragma warning disable CS0618
                o.Label
#pragma warning restore CS0618
            });
        builder.Entity<WalletLabelData>()
            .HasOne(o => o.WalletData)
            .WithMany(w => w.WalletLabels).OnDelete(DeleteBehavior.Cascade);
            
        if (databaseFacade.IsNpgsql())
        {
            builder.Entity<WalletScriptData>()
                .Property(o => o.Data)
                .HasColumnType("JSONB");
        }
    }

    public List<WalletTransactionData> WalletTransactions { get; set; }
    public List<WalletScriptData> WalletScripts { get; set; }
    public WalletData WalletData { get; set; }
}
