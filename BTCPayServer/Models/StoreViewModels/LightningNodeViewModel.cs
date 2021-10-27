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
        [Display(Name = "Enable LNURL")]
        public bool LNURLEnabled { get; set; }
        
        [Display(Name = "LNURL Classic Mode")]
        public bool LNURLBech32Mode { get; set; } = true;

        [Display(Name = "LNURL enabled for standard invoices")]
        public bool LNURLStandardInvoiceEnabled { get; set; } = true;
        
        [Display(Name = "Allow payee to pass a comment")]
        public bool LUD12Enabled { get; set; }
        
        [Display(Name = "Do not offer BOLT11 for standard invoices")]
        public bool DisableBolt11PaymentMethod { get; set; }
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
