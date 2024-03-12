using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Liquid.Models
{
    public class CustomLiquidAssetsSettings
    {
        public List<LiquidAssetConfiguration> Items { get; set; } = new List<LiquidAssetConfiguration>();

        public class LiquidAssetConfiguration
        {
            [Required] public string AssetId { get; set; }

            [Range(0, double.PositiveInfinity)] public int Divisibility { get; set; } = 8;

            [Required]
            [Display(Name = "Display name")]
            public string DisplayName { get; set; }

            [Display(Name = "Checkout icon url")] public string CryptoImagePath { get; set; }

            [Required]
            [Display(Name = "Currency code")]
            public string CryptoCode { get; set; }

            public string[] DefaultRateRules { get; set; }
        }
    }
}
