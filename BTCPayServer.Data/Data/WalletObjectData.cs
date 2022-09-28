using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class WalletObjectData
    {
        public class TransactionBlob
        {
            [JsonProperty("comment")]
            public string Comment { get; set; }
        }
        public class ObjectTypes
        {
            public const string Tx = "tx";
        }
        public string WalletId { get; set; }
        public string ObjectTypeId { get; set; }
        public string ObjectId { get; set; }
        public string Data { get; set; }

        public List<WalletTaintData> Taints { get; set; }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<WalletObjectData>().HasKey(o =>
                new
                {
                    o.WalletId,
                    o.ObjectTypeId,
                    o.ObjectId
                }
            );

            builder.Entity<WalletObjectData>()
                .HasMany(o => o.Taints)
                .WithOne(o => o.WalletObject);

            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<WalletObjectData>()
                                .Property(o => o.Data)
                                .HasColumnType("JSONB");
            }
        }
    }
}
