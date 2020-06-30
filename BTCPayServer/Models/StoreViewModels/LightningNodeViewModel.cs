using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.StoreViewModels
{
    public class LightningNodeViewModel
    {
        [Display(Name = "Connection string")]
        public string ConnectionString
        {
            get;
            set;
        }

        public string CryptoCode
        {
            get;
            set;
        }
        public string InternalLightningNode { get; internal set; }
        public bool SkipPortTest { get; set; }

        [Display(Name="Lightning enabled")]
        public bool Enabled { get; set; } = true;

        public string StoreId { get; set; }
    }
}
