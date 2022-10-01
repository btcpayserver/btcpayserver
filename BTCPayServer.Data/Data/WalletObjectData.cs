using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Data
{
    public class WalletObjectData
    {
        public class Types
        {
            public const string Label = "label";
            public const string Tx = "tx";
        }
        public string WalletId { get; set; }
        public string Type { get; set; }
        public string Id { get; set; }
        public string Data { get; set; }

        public List<WalletObjectLinkData> ChildLinks { get; set; }
        public List<WalletObjectLinkData> ParentLinks { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<WalletObjectData>().HasKey(o =>
            new
            {
                o.WalletId,
                o.Type,
                o.Id,
            });

            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<WalletObjectData>()
                                .Property(o => o.Data)
                                .HasColumnType("JSONB");
            }
        }
    }
}
