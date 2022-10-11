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
        public string ParentType { get; set; }
        public string ParentId { get; set; }
        public string ChildType { get; set; }
        public string ChildId { get; set; }
        public string Data { get; set; }

        public WalletObjectData Parent { get; set; }
        public WalletObjectData Child { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<WalletObjectLinkData>().HasKey(o =>
            new
            {
                o.WalletId,
                o.ParentType,
                o.ParentId,
                o.ChildType,
                o.ChildId,
            });
            builder.Entity<WalletObjectLinkData>().HasIndex(o => new
            {
                o.WalletId,
                o.ChildType,
                o.ChildId,
            });

            builder.Entity<WalletObjectLinkData>()
                .HasOne(o => o.Parent)
                .WithMany(o => o.ChildLinks)
                .HasForeignKey(o => new { o.WalletId, o.ParentType, o.ParentId })
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalletObjectLinkData>()
                .HasOne(o => o.Child)
                .WithMany(o => o.ParentLinks)
                .HasForeignKey(o => new { o.WalletId, o.ChildType, o.ChildId })
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
