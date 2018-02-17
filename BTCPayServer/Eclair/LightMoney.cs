using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Eclair
{
    public enum LightMoneyUnit : ulong
    {
        BTC = 100000000000,
        MilliBTC = 100000000,
        Bit = 100000,
        Satoshi = 1000,
        MilliSatoshi = 1
    }

    public class LightMoney : IComparable, IComparable<LightMoney>, IEquatable<LightMoney>
    {


        // for decimal.TryParse. None of the NumberStyles' composed values is useful for bitcoin style
        private const NumberStyles BitcoinStyle =
                          NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
                        | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;


        /// <summary>
        /// Parse a bitcoin amount (Culture Invariant)
        /// </summary>
        /// <param name="bitcoin"></param>
        /// <param name="nRet"></param>
        /// <returns></returns>
        public static bool TryParse(string bitcoin, out LightMoney nRet)
        {
            nRet = null;

            decimal value;
            if (!decimal.TryParse(bitcoin, BitcoinStyle, CultureInfo.InvariantCulture, out value))
            {
                return false;
            }

            try
            {
                nRet = new LightMoney(value, LightMoneyUnit.BTC);
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        /// <summary>
        /// Parse a bitcoin amount (Culture Invariant)
        /// </summary>
        /// <param name="bitcoin"></param>
        /// <returns></returns>
        public static LightMoney Parse(string bitcoin)
        {
            LightMoney result;
            if (TryParse(bitcoin, out result))
            {
                return result;
            }
            throw new FormatException("Impossible to parse the string in a bitcoin amount");
        }

        long _MilliSatoshis;
        public long MilliSatoshi
        {
            get
            {
                return _MilliSatoshis;
            }
            // used as a central point where long.MinValue checking can be enforced 
            private set
            {
                CheckLongMinValue(value);
                _MilliSatoshis = value;
            }
        }

        /// <summary>
        /// Get absolute value of the instance
        /// </summary>
        /// <returns></returns>
        public LightMoney Abs()
        {
            var a = this;
            if (a < LightMoney.Zero)
                a = -a;
            return a;
        }

        public LightMoney(int satoshis)
        {
            MilliSatoshi = satoshis;
        }

        public LightMoney(uint satoshis)
        {
            MilliSatoshi = satoshis;
        }

        public LightMoney(long satoshis)
        {
            MilliSatoshi = satoshis;
        }

        public LightMoney(ulong satoshis)
        {
            // overflow check. 
            // ulong.MaxValue is greater than long.MaxValue
            checked
            {
                MilliSatoshi = (long)satoshis;
            }
        }

        public LightMoney(decimal amount, LightMoneyUnit unit)
        {
            // sanity check. Only valid units are allowed
            CheckMoneyUnit(unit, "unit");
            checked
            {
                var satoshi = amount * (long)unit;
                MilliSatoshi = (long)satoshi;
            }
        }


        /// <summary>
        /// Split the Money in parts without loss
        /// </summary>
        /// <param name="parts">The number of parts (must be more than 0)</param>
        /// <returns>The splitted money</returns>
        public IEnumerable<LightMoney> Split(int parts)
        {
            if (parts <= 0)
                throw new ArgumentOutOfRangeException(nameof(parts), "Parts should be more than 0");
            long remain;
            long result = DivRem(_MilliSatoshis, parts, out remain);

            for (int i = 0; i < parts; i++)
            {
                yield return LightMoney.Satoshis(result + (remain > 0 ? 1 : 0));
                remain--;
            }
        }

        private static long DivRem(long a, long b, out long result)
        {
            result = a % b;
            return a / b;
        }

        public static LightMoney FromUnit(decimal amount, LightMoneyUnit unit)
        {
            return new LightMoney(amount, unit);
        }

        /// <summary>
        /// Convert Money to decimal (same as ToDecimal)
        /// </summary>
        /// <param name="unit"></param>
        /// <returns></returns>
        public decimal ToUnit(LightMoneyUnit unit)
        {
            CheckMoneyUnit(unit, "unit");
            // overflow safe because (long / int) always fit in decimal 
            // decimal operations are checked by default
            return (decimal)MilliSatoshi / (int)unit;
        }
        /// <summary>
        /// Convert Money to decimal (same as ToUnit)
        /// </summary>
        /// <param name="unit"></param>
        /// <returns></returns>
        public decimal ToDecimal(LightMoneyUnit unit)
        {
            return ToUnit(unit);
        }

        public static LightMoney Coins(decimal coins)
        {
            // overflow safe.
            // decimal operations are checked by default
            return new LightMoney(coins * (ulong)LightMoneyUnit.BTC, LightMoneyUnit.MilliBTC);
        }

        public static LightMoney Bits(decimal bits)
        {
            // overflow safe.
            // decimal operations are checked by default
            return new LightMoney(bits * (ulong)LightMoneyUnit.Bit, LightMoneyUnit.MilliBTC);
        }

        public static LightMoney Cents(decimal cents)
        {
            // overflow safe.
            // decimal operations are checked by default
            return new LightMoney(cents * (ulong)LightMoneyUnit.Bit, LightMoneyUnit.MilliBTC);
        }

        public static LightMoney Satoshis(decimal sats)
        {
            return new LightMoney(sats * (ulong)LightMoneyUnit.Satoshi, LightMoneyUnit.MilliBTC);
        }

        public static LightMoney Satoshis(ulong sats)
        {
            return new LightMoney(sats);
        }

        public static LightMoney Satoshis(long sats)
        {
            return new LightMoney(sats);
        }

        public static LightMoney MilliSatoshis(long msats)
        {
            return new LightMoney(msats);
        }

        public static LightMoney MilliSatoshis(ulong msats)
        {
            return new LightMoney(msats);
        }

        #region IEquatable<Money> Members

        public bool Equals(LightMoney other)
        {
            if (other == null)
                return false;
            return _MilliSatoshis.Equals(other._MilliSatoshis);
        }

        public int CompareTo(LightMoney other)
        {
            if (other == null)
                return 1;
            return _MilliSatoshis.CompareTo(other._MilliSatoshis);
        }

        #endregion

        #region IComparable Members

        public int CompareTo(object obj)
        {
            if (obj == null)
                return 1;
            LightMoney m = obj as LightMoney;
            if (m != null)
                return _MilliSatoshis.CompareTo(m._MilliSatoshis);
#if !(PORTABLE || NETCORE)
            return _MilliSatoshis.CompareTo(obj);
#else
			return _Satoshis.CompareTo((long)obj);
#endif
        }

        #endregion

        public static LightMoney operator -(LightMoney left, LightMoney right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return new LightMoney(checked(left._MilliSatoshis - right._MilliSatoshis));
        }
        public static LightMoney operator -(LightMoney left)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            return new LightMoney(checked(-left._MilliSatoshis));
        }
        public static LightMoney operator +(LightMoney left, LightMoney right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return new LightMoney(checked(left._MilliSatoshis + right._MilliSatoshis));
        }
        public static LightMoney operator *(int left, LightMoney right)
        {
            if (right == null)
                throw new ArgumentNullException("right");
            return LightMoney.Satoshis(checked(left * right._MilliSatoshis));
        }

        public static LightMoney operator *(LightMoney right, int left)
        {
            if (right == null)
                throw new ArgumentNullException("right");
            return LightMoney.Satoshis(checked(right._MilliSatoshis * left));
        }
        public static LightMoney operator *(long left, LightMoney right)
        {
            if (right == null)
                throw new ArgumentNullException("right");
            return LightMoney.Satoshis(checked(left * right._MilliSatoshis));
        }
        public static LightMoney operator *(LightMoney right, long left)
        {
            if (right == null)
                throw new ArgumentNullException("right");
            return LightMoney.Satoshis(checked(left * right._MilliSatoshis));
        }

        public static LightMoney operator /(LightMoney left, long right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            return new LightMoney(checked(left._MilliSatoshis / right));
        }

        public static bool operator <(LightMoney left, LightMoney right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return left._MilliSatoshis < right._MilliSatoshis;
        }
        public static bool operator >(LightMoney left, LightMoney right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return left._MilliSatoshis > right._MilliSatoshis;
        }
        public static bool operator <=(LightMoney left, LightMoney right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return left._MilliSatoshis <= right._MilliSatoshis;
        }
        public static bool operator >=(LightMoney left, LightMoney right)
        {
            if (left == null)
                throw new ArgumentNullException("left");
            if (right == null)
                throw new ArgumentNullException("right");
            return left._MilliSatoshis >= right._MilliSatoshis;
        }

        public static implicit operator LightMoney(long value)
        {
            return new LightMoney(value);
        }
        public static implicit operator LightMoney(int value)
        {
            return new LightMoney(value);
        }

        public static implicit operator LightMoney(uint value)
        {
            return new LightMoney(value);
        }

        public static implicit operator LightMoney(ulong value)
        {
            return new LightMoney(checked((long)value));
        }

        public static implicit operator long(LightMoney value)
        {
            return value.MilliSatoshi;
        }

        public static implicit operator ulong(LightMoney value)
        {
            return checked((ulong)value.MilliSatoshi);
        }

        public static implicit operator LightMoney(string value)
        {
            return LightMoney.Parse(value);
        }

        public override bool Equals(object obj)
        {
            LightMoney item = obj as LightMoney;
            if (item == null)
                return false;
            return _MilliSatoshis.Equals(item._MilliSatoshis);
        }
        public static bool operator ==(LightMoney a, LightMoney b)
        {
            if (Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a._MilliSatoshis == b._MilliSatoshis;
        }

        public static bool operator !=(LightMoney a, LightMoney b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return _MilliSatoshis.GetHashCode();
        }


        /// <summary>
        /// Returns a culture invariant string representation of Bitcoin amount
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return ToString(false, false);
        }

        /// <summary>
        /// Returns a culture invariant string representation of Bitcoin amount
        /// </summary>
        /// <param name="fplus">True if show + for a positive amount</param>
        /// <param name="trimExcessZero">True if trim excess zeroes</param>
        /// <returns></returns>
        public string ToString(bool fplus, bool trimExcessZero = true)
        {
            var fmt = string.Format(CultureInfo.InvariantCulture, "{{0:{0}{1}B}}",
                                    (fplus ? "+" : null),
                                    (trimExcessZero ? "2" : "11"));
            return string.Format(BitcoinFormatter.Formatter, fmt, _MilliSatoshis);
        }


        static LightMoney _Zero = new LightMoney(0);
        public static LightMoney Zero
        {
            get
            {
                return _Zero;
            }
        }

        internal class BitcoinFormatter : IFormatProvider, ICustomFormatter
        {
            public static readonly BitcoinFormatter Formatter = new BitcoinFormatter();

            public object GetFormat(Type formatType)
            {
                return formatType == typeof(ICustomFormatter) ? this : null;
            }

            public string Format(string format, object arg, IFormatProvider formatProvider)
            {
                if (!this.Equals(formatProvider))
                {
                    return null;
                }
                var i = 0;
                var plus = format[i] == '+';
                if (plus)
                    i++;
                int decPos = 0;
                if (int.TryParse(format.Substring(i, 1), out decPos))
                {
                    i++;
                }
                var unit = format[i];
                var unitToUseInCalc = LightMoneyUnit.BTC;
                switch (unit)
                {
                    case 'B':
                        unitToUseInCalc = LightMoneyUnit.BTC;
                        break;
                }
                var val = Convert.ToDecimal(arg, CultureInfo.InvariantCulture) / (long)unitToUseInCalc;
                var zeros = new string('0', decPos);
                var rest = new string('#', 11 - decPos);
                var fmt = plus && val > 0 ? "+" : string.Empty;

                fmt += "{0:0" + (decPos > 0 ? "." + zeros + rest : string.Empty) + "}";
                return string.Format(CultureInfo.InvariantCulture, fmt, val);
            }
        }

        /// <summary>
        /// Tell if amount is almost equal to this instance
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="dust">more or less amount</param>
        /// <returns>true if equals, else false</returns>
        public bool Almost(LightMoney amount, LightMoney dust)
        {
            if (amount == null)
                throw new ArgumentNullException("amount");
            if (dust == null)
                throw new ArgumentNullException("dust");
            return (amount - this).Abs() <= dust;
        }

        /// <summary>
        /// Tell if amount is almost equal to this instance
        /// </summary>
        /// <param name="amount"></param>
        /// <param name="margin">error margin (between 0 and 1)</param>
        /// <returns>true if equals, else false</returns>
        public bool Almost(LightMoney amount, decimal margin)
        {
            if (amount == null)
                throw new ArgumentNullException("amount");
            if (margin < 0.0m || margin > 1.0m)
                throw new ArgumentOutOfRangeException("margin", "margin should be between 0 and 1");
            var dust = LightMoney.Satoshis((decimal)this.MilliSatoshi * margin);
            return Almost(amount, dust);
        }

        public static LightMoney Min(LightMoney a, LightMoney b)
        {
            if (a == null)
                throw new ArgumentNullException("a");
            if (b == null)
                throw new ArgumentNullException("b");
            if (a <= b)
                return a;
            return b;
        }

        public static LightMoney Max(LightMoney a, LightMoney b)
        {
            if (a == null)
                throw new ArgumentNullException("a");
            if (b == null)
                throw new ArgumentNullException("b");
            if (a >= b)
                return a;
            return b;
        }

        private static void CheckLongMinValue(long value)
        {
            if (value == long.MinValue)
                throw new OverflowException("satoshis amount should be greater than long.MinValue");
        }

        private static void CheckMoneyUnit(LightMoneyUnit value, string paramName)
        {
            var typeOfMoneyUnit = typeof(LightMoneyUnit);
            if (!Enum.IsDefined(typeOfMoneyUnit, value))
            {
                throw new ArgumentException("Invalid value for MoneyUnit", paramName);
            }
        }

        #region IComparable Members

        int IComparable.CompareTo(object obj)
        {
            return this.CompareTo(obj);
        }

        #endregion
    }
}
