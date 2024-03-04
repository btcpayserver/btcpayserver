#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayServer.Services.Altcoins.Monero.Payments;
using BTCPayServer.Services.Altcoins.Zcash.Payments;

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
                   others.FirstOrDefault(f => f._CryptoCode == _CryptoCode);
        }

        public PaymentMethodId(string cryptoCode, string paymentType):
            this(cryptoCode, PaymentTypes.Parse(paymentType))
        {
        }
        public PaymentMethodId(string cryptoCode, PaymentType paymentType)
        {
            ArgumentNullException.ThrowIfNull(cryptoCode);
            ArgumentNullException.ThrowIfNull(paymentType);
            PaymentType = paymentType;
            _CryptoCode = cryptoCode.ToUpperInvariant();
        }

        static PaymentMethodId _BTCOnChain = new PaymentMethodId("BTC", PaymentTypes.BTCLike);
        public bool IsBTCOnChain
        {
            get
            {
                return _BTCOnChain == this;
            }
        }

        string _CryptoCode;
        public string CryptoCode => _CryptoCode;
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
            return $"{_CryptoCode}-{PaymentType}";
        }

        public string ToPrettyString()
        {
            return $"{_CryptoCode} ({PaymentType.ToPrettyString()})";
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
                type = MoneroPaymentType.Instance;
            if (parts[0].ToUpperInvariant() == "ZEC")
                type = ZcashPaymentType.Instance;
#endif
            if (parts.Length == 2)
            {
                if (parts[1] is "LightningLike" or "LightningNetwork" or "OffChain")
                    parts[1] = "LN";
                else if (parts[1] is "BitcoinLike" or "OnChain" or "BTCLike" or "On-Chain")
                    parts[1] = "CHAIN";
                else if (parts[1] == "LNURLPAY")
                    parts[1] = "LNURL";
                else if (parts[1] == "MoneroLike")
                    parts[1] = "CHAIN";
                else if (parts[1] == "ZcashLike")
                    parts[1] = "CHAIN";
                if (!PaymentTypes.TryParse(parts[1], out type))
                    return false;
            }
            if (parts.Length == 1)
                type = PaymentTypes.BTCLike;
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
