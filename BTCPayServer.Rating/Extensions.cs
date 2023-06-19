using System;

namespace BTCPayServer.Rating
{
    public static class Extensions
    {
        public static decimal RoundToSignificant(this decimal value, int divisibility)
        {
            return RoundToSignificant(value, ref divisibility);
        }
        public static decimal RoundToSignificant(this decimal value, ref int divisibility)
        {
            if (value != 0m)
            {
                while (true)
                {
                    var rounded = decimal.Round(value, divisibility, MidpointRounding.AwayFromZero);
                    if ((Math.Abs(rounded - value) / value) < 0.01m)
                    {
                        value = rounded;
                        break;
                    }
                    divisibility++;
                }
            }
            return value;
        }
    }
}
