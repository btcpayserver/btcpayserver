using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.StoreViewModels
{
    public enum LightningNodeType
    {
        Internal,
        Custom
    }

    public class LightningNodeViewModel
    {
        public LightningNodeType LightningNodeType { get; set; }
        public string StoreId { get; set; }
        public string CryptoCode { get; set; }
        public bool CanUseInternalNode { get; set; }
        public bool SkipPortTest { get; set; }
        public bool Enabled { get; set; } = true;

        [Display(Name = "Connection string")]
        public string ConnectionString { get; set; }
    }
}
