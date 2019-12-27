namespace BTCPayServer.Data
{
    public class RefundAddressesData
    {
        public string Id
        {
            get; set;
        }
        public string InvoiceDataId
        {
            get; set;
        }
        public InvoiceData InvoiceData
        {
            get; set;
        }
        public byte[] Blob
        {
            get; set;
        }
    }
}
