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
            public const string Payjoin = "payjoin";
            public const string Invoice = "invoice";
            public const string PaymentRequest = "payment-request";
            public const string App = "app";
            public const string PayjoinExposed = "pj-exposed";
            public const string Payout = "payout";
            public const string PullPayment = "pull-payment";
            public const string Script = "script";
            public const string Utxo = "utxo";
        }
        public string WalletId { get; set; }
        public string Type { get; set; }
        public string Id { get; set; }
        public string Data { get; set; }

        public List<WalletObjectLinkData> Bs { get; set; }
        public List<WalletObjectLinkData> As { get; set; }

        public IEnumerable<(string type, string id, string linkdata, string objectdata)> GetLinks()
        {
            if (Bs is not null)
                foreach (var c in Bs)
                {
                    yield return (c.BType, c.BId, c.Data, c.B?.Data);
                }
            if (As is not null)
                foreach (var c in As)
                {
                    yield return (c.AType, c.AId, c.Data, c.A?.Data);
                }
        }

        public IEnumerable<WalletObjectData> GetNeighbours()
        {
            if (Bs != null)
                foreach (var c in Bs)
                {
                    if (c.B != null)
                        yield return c.B;
                }
            if (As != null)
                foreach (var c in As)
                {
                    if (c.A != null)
                        yield return c.A;
                }
        }

        internal static void OnModelCreating(ModelBuilder builder, DatabaseFacade databaseFacade)
        {
            builder.Entity<WalletObjectData>().HasKey(o =>
            new
            {
                o.WalletId,
                o.Type,
                o.Id,
            });
            builder.Entity<WalletObjectData>().HasIndex(o =>
            new
            {
                o.Type,
                o.Id
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
