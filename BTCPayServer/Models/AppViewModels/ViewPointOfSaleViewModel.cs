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
            public string Id { get; set; }
            public ItemPrice Price { get; set; }
            public string Title { get; set; }
        }
        public string Title { get; set; }
        public Item[] Items { get; set; }
    }
}
