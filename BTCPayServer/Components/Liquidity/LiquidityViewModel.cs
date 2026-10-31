#nullable enable

namespace BTCPayServer.Components.Liquidity
{
    public class LiquidityViewModel
    {
        public bool HasLiquidityReport { get; set; }
        public LiquidityReport? LiquidityReport { get; set; }
        public string? Message { get; set; }
    }
}