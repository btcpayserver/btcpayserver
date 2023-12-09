using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.JsonConverters;
using BTCPayServer.Models;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc.Rendering;
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
            public string[] Categories { get; set; }
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

        public StoreBrandingViewModel StoreBranding { get; set; }
        public string StoreName { get; set; }
        public CurrencyInfoData CurrencyInfo { get; set; }
        public PosViewType ViewType { get; set; }
        public bool ShowCustomAmount { get; set; }
        public bool ShowDiscount { get; set; }
        public bool ShowSearch { get; set; } = true;
        public bool ShowCategories { get; set; } = true;
        public bool EnableTips { get; set; }
        public string Step { get; set; }
        public string Title { get; set; }
        Item[] _Items;
        public Item[] Items
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
        public string Description { get; set; }
        public SelectList AllCategories { get; set; }
        [Display(Name = "Custom CSS Code")]
        public RequiresRefundEmail RequiresRefundEmail { get; set; } = RequiresRefundEmail.InheritFromStore;
        public string StoreId { get; set; }
    }
}
