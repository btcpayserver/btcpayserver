using System.Globalization;

namespace BTCPayServer.Plugins.Zcash
{
    public class ZcashMoney
    {
        public static decimal Convert(long zat)
        {
            var amt = zat.ToString(CultureInfo.InvariantCulture).PadLeft(8, '0');
            amt = amt.Length == 8 ? $"0.{amt}" : amt.Insert(amt.Length - 8, ".");

            return decimal.Parse(amt, CultureInfo.InvariantCulture);
        }

        public static long Convert(decimal Zcash)
        {
            return System.Convert.ToInt64(Zcash * 100000000);
        }
    }
}
