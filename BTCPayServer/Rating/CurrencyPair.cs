using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Rating
{
    public class CurrencyPair
    {
        static readonly BTCPayNetworkProvider _NetworkProvider = new BTCPayNetworkProvider(NBitcoin.NetworkType.Mainnet);
        public CurrencyPair(string left, string right)
        {
            if (right == null)
                throw new ArgumentNullException(nameof(right));
            if (left == null)
                throw new ArgumentNullException(nameof(left));
            Right = right.ToUpperInvariant();
            Left = left.ToUpperInvariant();
        }
        public string Left { get; private set; }
        public string Right { get; private set; }

        public static CurrencyPair Parse(string str)
        {
            if (!TryParse(str, out var result))
                throw new FormatException("Invalid currency pair");
            return result;
        }
        public static bool TryParse(string str, out CurrencyPair value)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            value = null;
            str = str.Trim();
            if (str.Length > 12)
                return false;
            var splitted = str.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (splitted.Length == 2)
            {
                value = new CurrencyPair(splitted[0], splitted[1]);
                return true;
            }
            else if (splitted.Length == 1)
            {
                var currencyPair = splitted[0];
                if (currencyPair.Length < 6 || currencyPair.Length > 10)
                    return false;
                if (currencyPair.Length == 6)
                { 
                    value = new CurrencyPair(currencyPair.Substring(0,3), currencyPair.Substring(3, 3));
                    return true;
                }
                for (int i = 3; i < 5; i++)
                {
                    var potentialCryptoName = currencyPair.Substring(0, i);
                    var network = _NetworkProvider.GetNetwork(potentialCryptoName);
                    if (network != null)
                    {
                        value = new CurrencyPair(network.CryptoCode, currencyPair.Substring(i));
                        return true;
                    }
                }
            }

            return false;
        }


        public override bool Equals(object obj)
        {
            CurrencyPair item = obj as CurrencyPair;
            if (item == null)
                return false;
            return ToString().Equals(item.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        public static bool operator ==(CurrencyPair a, CurrencyPair b)
        {
            if (System.Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.ToString() == b.ToString();
        }

        public static bool operator !=(CurrencyPair a, CurrencyPair b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode(StringComparison.OrdinalIgnoreCase);
        }
        public override string ToString()
        {
            return $"{Left}_{Right}";
        }

        public CurrencyPair Inverse()
        {
            return new CurrencyPair(Right, Left);
        }
    }
}
