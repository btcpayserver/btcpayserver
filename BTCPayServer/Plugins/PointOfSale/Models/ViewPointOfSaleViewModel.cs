using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.JsonConverters;
using BTCPayServer.Services.Apps;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.PointOfSale.Models
{
    public class ViewPointOfSaleViewModel
    {
        public enum ItemPriceType
        {
            Topup,
            Minimum,
            Fixed
        }
        
        public class Item
        {
           
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string Description { get; set; }
            public string Id { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string Image { get; set; }
            [JsonConverter(typeof(StringEnumConverter))]
            public ItemPriceType PriceType { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            [JsonConverter(typeof(NumericStringJsonConverter))]
            public decimal? Price { get; set; }
            public string Title { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string BuyButtonText { get; set; }
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public int? Inventory { get; set; } = null;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string[] PaymentMethods { get; set; }
            public bool Disabled { get; set; } = false;
            
            [JsonExtensionData] public Dictionary<string, JToken> AdditionalData { get; set; }
        }

        public class CurrencyInfoData
        {
            public bool Prefixed { get; set; }
            public string CurrencySymbol { get; set; }
            public string ThousandSeparator { get; set; }
            public string DecimalSeparator { get; set; }
            public int Divisibility { get; set; }
            public bool SymbolSpace { get; set; }
        }

        public string LogoFileId { get; set; }
        public string CssFileId { get; set; }
        public string BrandColor { get; set; }
        public string StoreName { get; set; }
        public CurrencyInfoData CurrencyInfo { get; set; }
        public PosViewType ViewType { get; set; }
        public bool ShowCustomAmount { get; set; }
        public bool ShowDiscount { get; set; }
        public bool EnableTips { get; set; }
        public string Step { get; set; }
        public string Title { get; set; }
        public Item[] Items { get; set; }
        public string CurrencyCode { get; set; }
        public string CurrencySymbol { get; set; }
        public string AppId { get; set; }
        public string ButtonText { get; set; }
        public string CustomButtonText { get; set; }
        public string CustomTipText { get; set; }
        public int[] CustomTipPercentages { get; set; }

        [Display(Name = "Custom CSS URL")]
        public string CustomCSSLink { get; set; }
        public string CustomLogoLink { get; set; }
        public string Description { get; set; }
        [Display(Name = "Custom CSS Code")]
        public string EmbeddedCSS { get; set; }
        public RequiresRefundEmail RequiresRefundEmail { get; set; } = RequiresRefundEmail.InheritFromStore;
        public string StoreId { get; set; }
    }
}
