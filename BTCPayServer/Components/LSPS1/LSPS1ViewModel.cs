#nullable enable
using System;
using BTCPayServer.Lightning;

namespace BTCPayServer.Components.LSPS1
{
    public class LSPS1ViewModel
    {
        public bool HasLiquidityReport { get; set; }
        public LiquidityReport? LiquidityReport { get; set; }
        public string? Message { get; set; }
    }
}