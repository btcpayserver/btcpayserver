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
                return CryptoCode == "BTC" && PaymentType == BitcoinPaymentType.Instance;
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
            return PaymentType == BitcoinPaymentType.Instance ? CryptoCode : $"{CryptoCode}_{PaymentType}";
        }
        /// <summary>
        /// A string we can expose to Greenfield API, not subjected to internal legacy
        /// </summary>
        /// <returns></returns>
        public string ToStringNormalized()
        {
            return PaymentType.GetPaymentMethodId(this);
        }

        public string ToPrettyString()
        {
            return $"{CryptoCode} ({PaymentType.ToPrettyString()})";
        }
        
    }
}
