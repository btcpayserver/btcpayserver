using System.Reflection.Metadata;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public static class InvoiceDataExtensions
    {
        public static void SetBlob(this InvoiceData invoiceData, InvoiceEntity blob)
        {
            if (blob.Metadata is null)
                blob.Metadata = new InvoiceMetadata();
            invoiceData.HasTypedBlob<InvoiceEntity>().SetBlob(blob);
        }
        public static InvoiceEntity GetBlob(this InvoiceData invoiceData, BTCPayNetworkProvider networks)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (invoiceData.Blob is not null && invoiceData.Blob.Length != 0)
            {
                var entity = JsonConvert.DeserializeObject<InvoiceEntity>(ZipUtils.Unzip(invoiceData.Blob), InvoiceRepository.DefaultSerializerSettings);
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
            else
            {
                var entity = invoiceData.HasTypedBlob<InvoiceEntity>().GetBlob();
                entity.Networks = networks;
                return entity;
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }
        public static InvoiceState GetInvoiceState(this InvoiceData invoiceData)
        {
            return new InvoiceState(invoiceData.Status, invoiceData.ExceptionStatus);
        }
    }
}
