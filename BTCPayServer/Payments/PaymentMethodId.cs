#nullable enable
using System;
using System.Collections.Frozen;
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
        public T? FindNearest<T>(IEnumerable<T> others)
        {
            ArgumentNullException.ThrowIfNull(others);
            return 
                GetSimilarities([this], others)
                .OrderByDescending(o => o.similarity)
                .Select(o => o.b)
                .FirstOrDefault();
        }

        /// <summary>
        /// Returns the carthesian product of the two enumerables with the similarity between each pair's strings.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="U"></typeparam>
        /// <param name="aItems"></param>
        /// <param name="bItems"></param>
        /// <returns></returns>
        public static IEnumerable<(T a, U b, int similarity)> GetSimilarities<T, U>(IEnumerable<T> aItems, IEnumerable<U> bItems)
        {
            return from a in aItems
                   from b in bItems
                   select (a, b, CalculateDistance(a.ToString()!, b.ToString()!));
        }

        private static int CalculateDistance(string a, string b)
        {
            int similarity = 0;
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                if (a[i] == b[i])
                    similarity++;
                else
                    break;
            }
            if (a.Length == b.Length)
                similarity++;
            return similarity;
        }

        public PaymentMethodId(string id)
        {
            ArgumentNullException.ThrowIfNull(id);
			_Id = id;
        }

		string _Id;

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
            if (str.Length == 0)
            {
                paymentMethodId = null;
                return false;
            }
            paymentMethodId = null;
            var parts = str.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

            {
                if (parts is [var cryptoCode])
                {
                    if (LegacySupportedCryptos.Contains(cryptoCode))
                    {
                        paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
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
                        paymentMethodId = new PaymentMethodId($"{cryptoCode.ToUpperInvariant()}-{paymentType}");
                        return true;
                    }
                }
            }

            paymentMethodId = new PaymentMethodId(str);
            return true;
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
