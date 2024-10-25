#nullable enable
using System;
using System.Globalization;
using System.Linq;
using BTCPayServer.Plugins.Altcoins;
using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public static class MoneyExtensions
    {
        public static decimal GetValue(this IMoney value, BTCPayNetwork network) =>
        (network, value) switch
        {
            (not ElementsBTCPayNetwork, Money m) => m.ToDecimal(MoneyUnit.BTC),
            (_, null) => 0m,
            (ElementsBTCPayNetwork e, Money m) when e.IsNativeAsset => m.ToDecimal(MoneyUnit.BTC),
            (_, MoneyBag mb) => mb.Select(money => money.GetValue(network)).Sum(),
            (ElementsBTCPayNetwork e, AssetMoney m) when m.AssetId == e.AssetId => m.ToDecimal(e.Divisibility),
            (ElementsBTCPayNetwork e, AssetMoney m) when m.AssetId != e.AssetId => 0m,
            _ => throw new InvalidOperationException($"Cannot get an amount from {value} with network {network}")
        };
        public static uint256? GetAssetId(this IMoney value, BTCPayNetwork network) =>
        (network, value) switch
        {
            (ElementsBTCPayNetwork e, AssetMoney m) when m.AssetId == e.AssetId => m.AssetId,
            (ElementsBTCPayNetwork e, Money) when e.IsNativeAsset => e.AssetId,
            _ => null
        };
        public static bool IsCompatible(this IMoney value, BTCPayNetwork network) =>
        (network, value) switch
        {
            (not ElementsBTCPayNetwork, Money) => true,
            (ElementsBTCPayNetwork e, Money) when e.IsNativeAsset => true,
            (ElementsBTCPayNetwork e, AssetMoney m) when m.AssetId == e.AssetId => true,
            _ => false
        };

        public static string ShowMoney(this IMoney money, BTCPayNetwork network)
        {
            return money.GetValue(network).ShowMoney(network.Divisibility);
        }

        public static string ShowMoney(this Money money, int? divisibility)
        {
            return !divisibility.HasValue
                ? money.ToString()
                : money.ToDecimal(MoneyUnit.BTC).ShowMoney(divisibility.Value);
        }

        public static string ShowMoney(this decimal d, int divisibility)
        {
            return d.ToString(GetDecimalFormat(divisibility), CultureInfo.InvariantCulture);
        }

        private static string GetDecimalFormat(int divisibility)
        {
            var res = $"0{(divisibility > 0 ? "." : string.Empty)}";
            return res.PadRight(divisibility + res.Length, '0');
        }
    }
}
