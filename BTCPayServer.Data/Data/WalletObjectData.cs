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
        }
        public string WalletId { get; set; }
        public string Type { get; set; }
        public string Id { get; set; }
        public string Data { get; set; }

        public List<WalletObjectLinkData> ChildLinks { get; set; }
        public List<WalletObjectLinkData> ParentLinks { get; set; }

        public IEnumerable<(string type, string id, string linkdata, string objectdata)> GetLinks()
        {
            if (ChildLinks is not null)
                foreach (var c in ChildLinks)
                {
                    yield return (c.ChildType, c.ChildId, c.Data, c.Child?.Data);
                }
            if (ParentLinks is not null)
                foreach (var c in ParentLinks)
                {
                    yield return (c.ParentType, c.ParentId, c.Data, c.Parent?.Data);
                }
        }

        public IEnumerable<WalletObjectData> GetNeighbours()
        {
            if (ChildLinks != null)
                foreach (var c in ChildLinks)
                {
                    if (c.Child != null)
                        yield return c.Child;
                }
            if (ParentLinks != null)
                foreach (var c in ParentLinks)
                {
                    if (c.Parent != null)
                        yield return c.Parent;
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

            if (databaseFacade.IsNpgsql())
            {
                builder.Entity<WalletObjectData>()
                                .Property(o => o.Data)
                                .HasColumnType("JSONB");
            }
        }
    }
}
