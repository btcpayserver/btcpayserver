using System;
using System.Linq;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Rating
{
    public class CurrencyPair
    {
        public CurrencyPair(string left, string right)
        {
            ArgumentNullException.ThrowIfNull(right);
            ArgumentNullException.ThrowIfNull(left);
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
            ArgumentNullException.ThrowIfNull(str);
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
                    value = new CurrencyPair(currencyPair.Substring(0, 3), currencyPair.Substring(3, 3));
                    return true;
                }

                for (int i = 3; i < 5; i++)
                {
                    var potentialCryptoName = currencyPair.Substring(0, i);
                    var currency = CurrencyNameTable.Instance.GetCurrencyData(potentialCryptoName, false);
                    if (currency != null)
                    {
                        value = new CurrencyPair(currency.Code, currencyPair.Substring(i));
                        return true;
                    }
                }
            }
            else if (splitted.Length > 2)
            {
                // Some shitcoin have _ their own ticker name... Since we don't care about those, let's
                // parse it anyway assuming the first part is one currency.
                value = new CurrencyPair(splitted[0], string.Join("_", splitted.Skip(1).ToArray()));
                return true;
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
