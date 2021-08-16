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
        public bool LNURLEnabled { get; set; } = true;
        public bool LNURLBech32Mode { get; set; } = true;
        public bool LNURLStandardInvoiceEnabled { get; set; } = false;
        public bool LUD12Enabled { get; set; } = false;
        public LightningNodeType LightningNodeType { get; set; }
        [Display(Name = "Connection string")]
        public string ConnectionString { get; set; }
        public string CryptoCode { get; set; }
        public bool CanUseInternalNode { get; set; }
        public bool SkipPortTest { get; set; }
        public bool Enabled { get; set; } = true;
        public string StoreId { get; set; }
        public bool DisableBolt11PaymentMethod { get; set; }
    }
}
