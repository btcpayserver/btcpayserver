using System;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer
{
    public class LightningAddressSettings
        {
            public class LightningAddressItem
            {
                public string StoreId { get; set; }
                [Display(Name = "Invoice currency")]
                public string CurrencyCode { get; set; }

                [Display(Name = "Min sats")]
                [Range(1, double.PositiveInfinity)]
                public decimal? Min { get; set; }

                [Display(Name = "Max sats")]
                [Range(1, double.PositiveInfinity)]
                public decimal? Max { get; set; }
            }

            public ConcurrentDictionary<string, LightningAddressItem> Items { get; set; } =
                new ConcurrentDictionary<string, LightningAddressItem>();

            public ConcurrentDictionary<string, string[]> StoreToItemMap { get; set; } =
                new ConcurrentDictionary<string, string[]>();

            public override string ToString()
            {
                return null;
            }
        }
}
