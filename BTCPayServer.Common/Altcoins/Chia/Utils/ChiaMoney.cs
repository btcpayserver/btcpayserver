using System.Globalization;

namespace BTCPayServer.Common.Altcoins.Chia.Utils
{
    public class ChiaMoney
    {
        public static decimal Convert(ulong mojo)
        {
            var amt = mojo.ToString(CultureInfo.InvariantCulture).PadLeft(12, '0');
            amt = amt.Length == 12 ? $"0.{amt}" : amt.Insert(amt.Length - 12, ".");

            return decimal.Parse(amt, CultureInfo.InvariantCulture);
        }

        public static long Convert(decimal chia)
        {
            return System.Convert.ToInt64(chia * 1_000_000_000_000);
        }
    }
}
