using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.LNbank.Services
{
    public class LightningInvoicePayRequest
    {
        [Required]
        public string PaymentRequest { get; set; }
    }
}
