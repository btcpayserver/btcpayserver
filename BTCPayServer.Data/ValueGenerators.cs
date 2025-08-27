using System;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Data;

public class ValueGenerators
{
    class WithPrefixGen(string prefix) : ValueGenerator
    {
        protected override object NextValue(EntityEntry entry)
            => $"{prefix}_{Encoders.Base58.EncodeData(RandomUtils.GetBytes(13))}";

        public override bool GeneratesTemporaryValues => false;
    }

    public static Func<IProperty, ITypeBase, ValueGenerator> WithPrefix(string prefix)
        => (_, _) => new WithPrefixGen(prefix);
}
