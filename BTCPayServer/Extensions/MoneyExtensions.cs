using System;
using System.Globalization;
using System.Linq;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public static class MoneyExtensions
    {
        public static decimal GetValue(this IMoney m, BTCPayNetwork network = null)
        {
            switch (m)
            {
                case Money money:
                    return money.ToDecimal(MoneyUnit.BTC);
                case MoneyBag mb:
                    return mb.Select(money => money.GetValue(network)).Sum();
                case AssetMoney assetMoney:
                    if (network is ElementsBTCPayNetwork elementsBTCPayNetwork)
                    {
                        return elementsBTCPayNetwork.AssetId == assetMoney.AssetId
                            ? Convert(assetMoney.Quantity, elementsBTCPayNetwork.Divisibility)
                            : 0;
                    }
                    throw new NotSupportedException("IMoney type not supported");
                default:
                    throw new NotSupportedException("IMoney type not supported");
            }
        }
        
        public static decimal Convert(long sats, int divisibility = 8)
        {
            var amt = sats.ToString(CultureInfo.InvariantCulture).PadLeft(divisibility, '0');
            amt = amt.Length == divisibility ? $"0.{amt}" : amt.Insert(amt.Length - divisibility, ".");

            return decimal.Parse(amt, CultureInfo.InvariantCulture);
        }
    }
}
