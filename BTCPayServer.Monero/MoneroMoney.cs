using System.Globalization;

namespace BTCPayServer.Monero
{
    public class MoneroMoney
    {
        public static decimal Convert(long atoms)
        {
            var amt = atoms.ToString().PadLeft(12, '0');
            amt = amt.Length == 12 ? $"0.{amt}" : amt.Insert(amt.Length - 12, ".");

            return decimal.Parse(amt, CultureInfo.InvariantCulture);
        }

        public static long Convert(decimal monero)
        {
            return System.Convert.ToInt64(monero);
        }
    }
}
