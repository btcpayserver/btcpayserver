using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.LNbank.Services
{
    public class LightningInvoicePayRequest
    {
        [Required]
        public string WalletId { get; set; }
        [Required]
        public string PaymentRequest { get; set; }
    }
}
