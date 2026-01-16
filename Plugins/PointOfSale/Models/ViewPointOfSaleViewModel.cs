using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Plugins.PointOfSale.Models
{
    public class ViewPointOfSaleViewModel
    {
        public class CurrencyInfoData
        {
            public bool Prefixed { get; set; }
            public string CurrencySymbol { get; set; }
            public string ThousandSeparator { get; set; }
            public string DecimalSeparator { get; set; }
            public int Divisibility { get; set; }
            public bool SymbolSpace { get; set; }
        }

        public StoreBrandingViewModel StoreBranding { get; set; }
        public string StoreName { get; set; }
        public CurrencyInfoData CurrencyInfo { get; set; }
        public PosViewType ViewType { get; set; }
        public bool ShowItems { get; set; }
        public bool ShowCustomAmount { get; set; }
        public bool ShowDiscount { get; set; }
        public bool ShowSearch { get; set; } = true;
        public bool ShowCategories { get; set; } = true;
        public bool EnableTips { get; set; }
        public string Step { get; set; }
        public string Title { get; set; }
        AppItem[] _Items;
        public AppItem[] Items
        {
            get
            {
                return _Items;
            }
            set
            {
                _Items = value;
                UpdateGroups();
            }
        }

        private void UpdateGroups()
        {
            AllCategories = null;
            if (Items is null)
                return;
            var groups = Items.SelectMany(g => g.Categories ?? Array.Empty<string>())
                              .ToHashSet()
                              .Select(o => new KeyValuePair<string, string>(o, o))
                              .ToList();
            if (groups.Count == 0)
                return;
            groups.Insert(0, new KeyValuePair<string, string>("All", "*"));
            AllCategories = new SelectList(groups, "Value", "Key", "*");
        }

        public string CurrencyCode { get; set; }
        public string CurrencySymbol { get; set; }
        public string AppId { get; set; }
        public string ButtonText { get; set; }
        public string CustomButtonText { get; set; }
        public string CustomTipText { get; set; }
        public int[] CustomTipPercentages { get; set; }
        public string HtmlLang { get; set; }
        public string HtmlMetaTags{ get; set; }
        public string Description { get; set; }
        public SelectList AllCategories { get; set; }
        public string StoreId { get; set; }
        public decimal DefaultTaxRate { get; set; }
    }
}
