using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class WalletTaintData
    {
        public string WalletId { get; set; }
        public string LabelId { get; set; }
        public string ObjectTypeId { get; set; }
        public string ObjectId { get; set; }
        public string TaintTypeId { get; set; }
        public string TaintId { get; set; }
        public int Stickiness { get; set; }
        public string Data { get; set; }

        public WalletObjectData WalletObject { get; set; }
        public WalletLabelData Label { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<WalletTaintData>().HasKey(o =>
            new
            {
                o.WalletId,
                o.ObjectTypeId,
                o.ObjectId,
                o.TaintTypeId,
                o.TaintId
            });

            builder.Entity<WalletTaintData>()
                .HasOne(o => o.WalletObject)
                .WithMany(o => o.Taints)
                .HasForeignKey(o => new
                {
                    o.WalletId,
                    o.ObjectTypeId,
                    o.ObjectId
                })
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalletTaintData>()
                .HasOne(o => o.Label)
                .WithMany(o => o.Taints)
                .HasForeignKey(o => new
                {
                    o.WalletId,
                    o.LabelId
                })
                .OnDelete(DeleteBehavior.Cascade);


            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<WalletTaintData>()
                                .Property(o => o.Data)
                                .HasColumnType("JSONB");
            }
        }
    }
}
