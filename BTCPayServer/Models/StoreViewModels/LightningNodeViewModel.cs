using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.StoreViewModels
{
    public enum LightningNodeType
    {
        None,
        Internal,
        Custom
    }

    public class LightningNodeViewModel
    {
        public LightningNodeType LightningNodeType { get; set; }
        [Display(Name = "Connection string")]
        public string ConnectionString { get; set; }
        public string CryptoCode { get; set; }
        public bool CanUseInternalNode { get; set; }
        public bool SkipPortTest { get; set; }
        public bool Enabled { get; set; } = true;
        public string StoreId { get; set; }
    }
}
