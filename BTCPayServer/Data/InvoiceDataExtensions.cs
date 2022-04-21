using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Data
{
    public static class InvoiceDataExtensions
    {
        public static InvoiceEntity GetBlob(this InvoiceData invoiceData, BTCPayNetworkProvider networks)
        {

            var entity = InvoiceRepository.FromBytes<InvoiceEntity>(invoiceData.Blob);
            entity.Networks = networks;
            if (entity.Metadata is null)
            {
                if (entity.Version < InvoiceEntity.GreenfieldInvoices_Version)
                {
                    entity.MigrateLegacyInvoice();
                }
                else
                {
                    entity.Metadata = new InvoiceMetadata();
                }
            }
            return entity;
        }
        public static InvoiceState GetInvoiceState(this InvoiceData invoiceData)
        {
            return new InvoiceState(invoiceData.Status, invoiceData.ExceptionStatus);
        }
    }
}
