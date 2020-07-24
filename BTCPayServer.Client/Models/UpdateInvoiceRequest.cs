namespace BTCPayServer.Client.Models
{
    public class UpdateInvoiceRequest
    {
        public bool? Archived { get; set; }
        public InvoiceStatus? Status { get; set; }
        public string Email { get; set; }
    }
}
