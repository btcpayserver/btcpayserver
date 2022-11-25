using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class WalletObjectLinkData
    {
        public string WalletId { get; set; }
        public string AType { get; set; }
        public string AId { get; set; }
        public string BType { get; set; }
        public string BId { get; set; }
        public string Data { get; set; }

        public WalletObjectData A { get; set; }
        public WalletObjectData B { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<WalletObjectLinkData>().HasKey(o =>
            new
            {
                o.WalletId,
                o.AType,
                o.AId,
                o.BType,
                o.BId,
            });
            builder.Entity<WalletObjectLinkData>().HasIndex(o => new
            {
                o.WalletId,
                o.BType,
                o.BId,
            });

            builder.Entity<WalletObjectLinkData>()
                .HasOne(o => o.A)
                .WithMany(o => o.Bs)
                .HasForeignKey(o => new { o.WalletId, o.AType, o.AId })
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalletObjectLinkData>()
                .HasOne(o => o.B)
                .WithMany(o => o.As)
                .HasForeignKey(o => new { o.WalletId, o.BType, o.BId })
                .OnDelete(DeleteBehavior.Cascade);

            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<WalletObjectLinkData>()
                                .Property(o => o.Data)
                                .HasColumnType("JSONB");
            }
        }
    }
}
