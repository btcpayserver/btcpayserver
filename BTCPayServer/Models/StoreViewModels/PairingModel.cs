using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.StoreViewModels
{
    public class PairingModel
    {
        public class StoreViewModel
        {
            public string Name
            {
                get; set;
            }
            public string Id
            {
                get; set;
            }
        }
        public string Id
        {
            get; set;
        }
        public string Label
        {
            get; set;
        }
        public string Facade
        {
            get; set;
        }
        public string SIN
        {
            get; set;
        }
        public StoreViewModel[] Stores
        {
            get;
            set;
        }

        [Display(Name = "Pair to")]
        [Required]
        public string SelectedStore
        {
            get; set;
        }
    }
}
