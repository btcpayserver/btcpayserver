#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace BTCPayServer.Payments
{

    /// <summary>
    /// A value object which represent a crypto currency with his payment type (ie, onchain or offchain)
    /// </summary>
    public class PaymentMethodId
    {
        public PaymentMethodId? FindNearest(IEnumerable<PaymentMethodId> others)
        {
            ArgumentNullException.ThrowIfNull(others);
            return others.FirstOrDefault(f => f == this) ??
                   others.FirstOrDefault(f => f.CryptoCode == CryptoCode);
        }
        public PaymentMethodId(string cryptoCode, PaymentType paymentType)
        {
            ArgumentNullException.ThrowIfNull(cryptoCode);
            ArgumentNullException.ThrowIfNull(paymentType);
            PaymentType = paymentType;
            CryptoCode = cryptoCode.ToUpperInvariant();
        }

        public bool IsBTCOnChain
        {
            get
            {
                return CryptoCode == "BTC" && PaymentType == PaymentTypes.BTCLike;
            }
        }

        public string CryptoCode { get; private set; }
        public PaymentType PaymentType { get; private set; }


        public override bool Equals(object? obj)
        {
            if (obj is PaymentMethodId id)
                return ToString().Equals(id.ToString(), StringComparison.OrdinalIgnoreCase);
            return false;
        }
        public static bool operator ==(PaymentMethodId? a, PaymentMethodId? b)
        {
            if (a is null && b is null)
                return true;
            if (a is PaymentMethodId ai && b is PaymentMethodId bi)
                return ai.Equals(bi);
            return false;
        }

        public static bool operator !=(PaymentMethodId? a, PaymentMethodId? b)
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
            //BTCLike case is special because it is in legacy mode.
            return PaymentType == PaymentTypes.BTCLike ? CryptoCode : $"{CryptoCode}_{PaymentType}";
        }
        /// <summary>
        /// A string we can expose to Greenfield API, not subjected to internal legacy
        /// </summary>
        /// <returns></returns>
        public string ToStringNormalized()
        {
            if (PaymentType == PaymentTypes.BTCLike)
                return CryptoCode;
#if ALTCOINS
            if (CryptoCode == "XMR" && PaymentType == PaymentTypes.MoneroLike)
                return CryptoCode;
            if ((CryptoCode == "YEC" || CryptoCode == "ZEC") && PaymentType == PaymentTypes.ZcashLike)
                return CryptoCode;
#endif
            return $"{CryptoCode}-{PaymentType.ToStringNormalized()}";
        }

        public string ToPrettyString()
        {
            return $"{CryptoCode} ({PaymentType.ToPrettyString()})";
        }
        static char[] Separators = new[] { '_', '-' };
        public static PaymentMethodId? TryParse(string? str)
        {
            TryParse(str, out var r);
            return r;
        }
        public static bool TryParse(string? str, [MaybeNullWhen(false)] out PaymentMethodId paymentMethodId)
        {
            str ??= "";
            paymentMethodId = null;
            var parts = str.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > 2)
                return false;
            PaymentType type = PaymentTypes.BTCLike;
#if ALTCOINS
            if (parts[0].ToUpperInvariant() == "XMR")
                type = PaymentTypes.MoneroLike;
            if (parts[0].ToUpperInvariant() == "ZEC")
                type = PaymentTypes.ZcashLike;
#endif
            if (parts.Length == 2)
            {
                if (!PaymentTypes.TryParse(parts[1], out type))
                    return false;
            }
            paymentMethodId = new PaymentMethodId(parts[0], type);
            return true;
        }
        public static PaymentMethodId Parse(string str)
        {
            if (!TryParse(str, out var result))
                throw new FormatException("Invalid PaymentMethodId");
            return result;
        }
    }
}
