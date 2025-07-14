using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public class WalletObjectData : IEqualityComparer<WalletObjectData>
    {
        public class Types
        {
            public static readonly HashSet<string> AllTypes;
            static Types()
            {
                AllTypes = typeof(Types).GetFields()
                    .Where(f => f.FieldType == typeof(string))
                    .Select(f => (string)f.GetValue(null)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            public const string Label = "label";
            public const string Tx = "tx";
            public const string CPFP = "CPFP";
            public const string RBF = "RBF";
            public const string Payjoin = "payjoin";
            public const string Invoice = "invoice";
            public const string PaymentRequest = "payment-request";
            public const string App = "app";
            public const string PayjoinExposed = "pj-exposed";
            public const string Payout = "payout";
            public const string PullPayment = "pull-payment";
            public const string Address = "address";
            public const string Utxo = "utxo";
        }
        public string WalletId { get; set; }
        public string Type { get; set; }
        public string Id { get; set; }
        public string Data { get; set; }
#nullable enable
        public JObject? GetData() => Data is null ? null : JObject.Parse(Data);
#nullable restore
        public List<WalletObjectLinkData> Bs { get; set; }
        public List<WalletObjectLinkData> As { get; set; }
#nullable enable
        [Timestamp]
        // With this, update of WalletObjectData will fail if the row was modified by another process
        public uint XMin { get; set; }

        public IEnumerable<(string type, string id, JObject? linkdata, JObject? objectdata)> GetLinks()
        {
            static JObject? AsJObj(string? data) => data is null ? null : JObject.Parse(data);
            if (Bs is not null)
                foreach (var c in Bs)
                {
                    yield return (c.BType, c.BId, AsJObj(c.Data), AsJObj(c.B?.Data));
                }
            if (As is not null)
                foreach (var c in As)
                {
                    yield return (c.AType, c.AId, AsJObj(c.Data), AsJObj(c.A?.Data));
                }
        }
#nullable restore
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

            builder.Entity<WalletObjectData>()
                .Property(o => o.Data)
                .HasColumnType("JSONB");
        }

        public bool Equals(WalletObjectData x, WalletObjectData y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            if (x.GetType() != y.GetType()) return false;
            return string.Equals(x.WalletId, y.WalletId, StringComparison.InvariantCultureIgnoreCase) &&
                   string.Equals(x.Type, y.Type, StringComparison.InvariantCultureIgnoreCase) &&
                   string.Equals(x.Id, y.Id, StringComparison.InvariantCultureIgnoreCase);
        }

        public int GetHashCode(WalletObjectData obj)
        {
            HashCode hashCode = new HashCode();
            hashCode.Add(obj.WalletId, StringComparer.InvariantCultureIgnoreCase);
            hashCode.Add(obj.Type, StringComparer.InvariantCultureIgnoreCase);
            hashCode.Add(obj.Id, StringComparer.InvariantCultureIgnoreCase);
            return hashCode.ToHashCode();
        }
    }
}
