using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.Plugins.PointOfSale.Models
{
    public class ViewPointOfSaleViewModel
    {
        public class Item
        {
            public class ItemPrice
            {
                public enum ItemPriceType
                {
                    Topup,
                    Minimum,
                    Fixed
                }

                public ItemPriceType Type { get; set; }
                public string Formatted { get; set; }
                public decimal? Value { get; set; }
            }
            public string Description { get; set; }
            public string Id { get; set; }
            public string Image { get; set; }
            public ItemPrice Price { get; set; }
            public string Title { get; set; }
            public string BuyButtonText { get; set; }
            public int? Inventory { get; set; } = null;
            public string[] PaymentMethods { get; set; }
            public bool Disabled { get; set; } = false;
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
