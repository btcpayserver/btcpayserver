using System.Globalization;

namespace BTCPayServer.Services.Altcoins.Monero.Utils
{
    public class MoneroMoney
    {
        public static decimal Convert(long piconero)
        {
            var amt = piconero.ToString(CultureInfo.InvariantCulture).PadLeft(12, '0');
            amt = amt.Length == 12 ? $"0.{amt}" : amt.Insert(amt.Length - 12, ".");

            return decimal.Parse(amt, CultureInfo.InvariantCulture);
        }

        public static long Convert(decimal monero)
        {
            return System.Convert.ToInt64(monero * 1000000000000);
        }
    }
}
