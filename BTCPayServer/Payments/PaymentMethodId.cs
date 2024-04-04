#nullable enable
using System;
using System.Collections.Frozen;
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
        public PaymentMethodId(string cryptoCode, string paymentType)
        {
            ArgumentNullException.ThrowIfNull(cryptoCode);
            ArgumentNullException.ThrowIfNull(paymentType);
			_CryptoCode = cryptoCode.ToUpperInvariant();
			_Id = $"{_CryptoCode}-{paymentType}";
        }

		string _Id;
        string _CryptoCode;
        public string CryptoCode => _CryptoCode;


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
            return _Id;
        }
        static char[] Separators = new[] { '_', '-' };
        public static PaymentMethodId? TryParse(string? str)
        {
            TryParse(str, out var r);
            return r;
        }

        static readonly FrozenSet<string> LegacySupportedCryptos = new HashSet<string>()
        {
            "XMR",
            "ZEC",
            "LCAD",
            "ETB",
            "LBTC",
            "USDt",
            "MONA",
            "LTC",
            "GRS",
            "DOGE",
            "DASH",
            "BTG",
            "BTC"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        public static bool TryParse(string? str, [MaybeNullWhen(false)] out PaymentMethodId paymentMethodId)
        {
            str ??= "";
            str = str.Trim();
            paymentMethodId = null;
            var parts = str.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

            {
                if (parts is [var cryptoCode])
                {
                    if (LegacySupportedCryptos.Contains(cryptoCode))
                    {
                        paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode.ToUpperInvariant());
                        return true;
                    }
                }
            }

            {
                if (parts is [var cryptoCode, var paymentType])
                {
                    if (LegacySupportedCryptos.Contains(cryptoCode))
                    {
						var type = GetPaymentType(paymentType);
						if (type != null)
						{
							paymentMethodId = type.GetPaymentMethodId(cryptoCode);
							return true;
						}
                        paymentMethodId = new PaymentMethodId(cryptoCode, paymentType);
                        return true;
                    }
                }
            }

            return false;
        }

        private static PaymentType? GetPaymentType(string paymentType)
        {
            return paymentType.ToLowerInvariant() switch
            {
                "lightninglike" or "lightningnetwork" or "offchain" or "off-chain" => PaymentTypes.LN,
                "bitcoinlike" or "onchain" or "btclike" or "on-chain" or "monerolike" or "zcashlike" => PaymentTypes.CHAIN,
                "lnurlpay" => PaymentTypes.LNURL,
                _ => null
            };
        }

        public static PaymentMethodId Parse(string str)
        {
            if (!TryParse(str, out var result))
                throw new FormatException("Invalid PaymentMethodId");
            return result;
        }
    }
}
