using NBitcoin;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoresViewModel
    {
        public int Skip
        {
            get; set;
        }
        public int Count
        {
            get; set;
        }
        public int Total
        {
            get; set;
        }
        // If false only display stores current user owns.
        // If true and user has an admin role display all stores.
        [Display(Name = "Show All")]
        public bool ShowAll
        {
            get; set;
        }

        public List<StoreViewModel> Stores
        {
            get; set;
        } = new List<StoreViewModel>();

        public class StoreViewModel
        {
            public string Name
            {
                get; set;
            }

            public string WebSite
            {
                get; set;
            }

            public string Id
            {
                get; set;
            }
            public bool IsOwner
            {
                get;
                set;
            }
        }
    }
}
