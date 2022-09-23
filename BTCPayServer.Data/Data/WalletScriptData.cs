using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data;

public class WalletScriptData
{
    public string WalletDataId { get; set; }
    public WalletData WalletData { get; set; }
    public string Script { get; set; }
    public string Data { get; set; }
    public List<WalletLabelData> WalletLabels { get; set; }

    public List<WalletTransactionData> WalletTransactions { get; set; }

    internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
    {
        builder.Entity<WalletScriptData>()
            .HasKey(o => new
            {
                o.WalletDataId,
#pragma warning disable CS0618
                o.Script
#pragma warning restore CS0618
            });
        builder.Entity<WalletScriptData>()
            .HasOne(o => o.WalletData)
            .WithMany(w => w.WalletScripts).OnDelete(DeleteBehavior.Cascade);
        
        builder.Entity<WalletScriptData>()
            .HasMany<WalletTransactionData>(o => o.WalletTransactions)
            .WithMany(w => w.WalletScripts);
            
        if (databaseFacade.IsNpgsql())
        {
            builder.Entity<WalletScriptData>()
                .Property(o => o.Data)
                .HasColumnType("JSONB");
        }
    }
}
