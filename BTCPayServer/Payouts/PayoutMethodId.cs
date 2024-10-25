#nullable enable
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayServer.Payments;

namespace BTCPayServer.Payouts
{

    /// <summary>
    /// A value object which represent a crypto currency with his payment type (ie, onchain or offchain)
    /// </summary>
    public class PayoutMethodId
    {
        PayoutMethodId(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
			_Id = id;
        }

        string _Id;


        public override bool Equals(object? obj)
        {
            if (obj is PayoutMethodId id)
                return ToString().Equals(id.ToString(), StringComparison.OrdinalIgnoreCase);
            return false;
        }
        public static bool operator ==(PayoutMethodId? a, PayoutMethodId? b)
        {
            if (a is null && b is null)
                return true;
            if (a is PayoutMethodId ai && b is PayoutMethodId bi)
                return ai.Equals(bi);
            return false;
        }

        public static bool operator !=(PayoutMethodId? a, PayoutMethodId? b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
#pragma warning disable CA1307 // Specify StringComparison
            return ToString().GetHashCode();
#pragma warning restore CA1307 // Specify StringComparison
        }

        public override string ToString()
        {
            return _Id;
        }
        static char[] Separators = new[] { '_', '-' };
        public static PayoutMethodId? TryParse(string? str)
        {
            TryParse(str, out var r);
            return r;
        }

        public static bool TryParse(string? str, [MaybeNullWhen(false)] out PayoutMethodId payoutMethodId)
        {
            payoutMethodId = null;
            if (!Payments.PaymentMethodId.TryParse(str, out var result))
                return false;
            var payoutId = result.ToString();
            // -LNURL should just be -LN
            var lnUrlSuffix = $"-{Payments.PaymentTypes.LNURL.ToString()}";
            if (payoutId.EndsWith(lnUrlSuffix, StringComparison.Ordinal))
                payoutId = payoutId.Substring(payoutId.Length - lnUrlSuffix.Length) + $"-{Payments.PaymentTypes.LN}";

            payoutMethodId = new PayoutMethodId(payoutId);
            return true;
        }
        public static PayoutMethodId Parse(string str)
        {
            if (!TryParse(str, out var result))
                throw new FormatException("Invalid PayoutMethodId");
            return result;
        }
    }
}
