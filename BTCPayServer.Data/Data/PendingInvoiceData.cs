namespace BTCPayServer.Data
{
    public class PendingInvoiceData
    {
        public string Id
        {
            get; set;
        }
        public InvoiceData InvoiceData { get; set; }
    }
}
