using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Data
{
    public static class InvoiceDataExtensions
    {
        public static InvoiceEntity GetBlob(this Data.InvoiceData invoiceData, BTCPayNetworkProvider networks)
        {
            var entity = NBitcoin.JsonConverters.Serializer.ToObject<InvoiceEntity>(ZipUtils.Unzip(invoiceData.Blob), null);
            entity.Networks = networks;
            return entity;
        }
        public static InvoiceState GetInvoiceState(this InvoiceData invoiceData)
        {
            return new InvoiceState(invoiceData.Status, invoiceData.ExceptionStatus);
        }
    }
}
