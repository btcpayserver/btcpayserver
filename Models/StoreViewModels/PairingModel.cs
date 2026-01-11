using System.ComponentModel.DataAnnotations;

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
        public string StoreId
        {
            get; set;
        }
    }
}
