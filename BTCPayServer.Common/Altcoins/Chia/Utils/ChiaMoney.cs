using System.Globalization;

namespace BTCPayServer.Services.Altcoins.Chia.Utils
{
    public class ChiaMoney
    {
        public static decimal Convert(ulong mojo)
        {
            var amt = mojo.ToString(CultureInfo.InvariantCulture).PadLeft(12, '0');
            amt = amt.Length == 12 ? $"0.{amt}" : amt.Insert(amt.Length - 12, ".");

            return decimal.Parse(amt, CultureInfo.InvariantCulture);
        }

        public static long Convert(decimal Chia)
        {
            return System.Convert.ToInt64(Chia * 1000000000000);
        }
    }
}
