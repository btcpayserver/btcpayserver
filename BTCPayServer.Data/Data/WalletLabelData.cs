using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class WalletLabelData
    {
        public string WalletId { get; set; }
        public string LabelId { get; set; }
        public string Data { get; set; }

        public List<WalletTaintData> Taints { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<WalletLabelData>().HasKey(o =>
            new
            {
                o.WalletId,
                o.LabelId
            });

            builder.Entity<WalletLabelData>()
                .HasMany(o => o.Taints)
                .WithOne(o => o.Label)
                .OnDelete(DeleteBehavior.Cascade);


            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<WalletLabelData>()
                                .Property(o => o.Data)
                                .HasColumnType("JSONB");
            }
        }
    }
}
