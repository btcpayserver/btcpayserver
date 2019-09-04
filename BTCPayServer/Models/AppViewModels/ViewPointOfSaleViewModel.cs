using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.AppViewModels
{
    public class ViewPointOfSaleViewModel
    {
        public class Item
        {
            public class ItemPrice
            {
                public string Formatted { get; set; }
                public decimal Value { get; set; }
            }
            public string Description { get; set; }
            public string Id { get; set; }
            public string Image { get; set; }
            public ItemPrice Price { get; set; }
            public string Title { get; set; }
            public bool Custom { get; set; }
            public int? Inventory { get; set; } = null;
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

        public bool EnableShoppingCart { get; set; }
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

        public string CustomCSSLink { get; set; }
        public string Description { get; set; }
        public string EmbeddedCSS { get; set; }
    }
}
