#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Services.Apps
{
    public abstract class AppBaseType
    {
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public abstract Task<object?> GetInfo(AppData appData);
        public abstract Task<string> ConfigureLink(AppData app);
        public abstract Task<string> ViewLink(AppData app);
        public abstract Task SetDefaultSettings(AppData appData, string defaultCurrency);
    }
    public interface IHasSaleStatsAppType
    {
        Task<SalesStats> GetSalesStats(AppData app, InvoiceEntity[] paidInvoices, int numberOfDays);
    }
    public interface IHasItemStatsAppType
    {
        Task<IEnumerable<ItemStats>> GetItemStats(AppData appData, InvoiceEntity[] invoiceEntities);
    }

    public enum RequiresRefundEmail
    {
        [Display(Name = "Inherit from store settings")]
        InheritFromStore,
        [Display(Name = "On")]
        On,
        [Display(Name = "Off")]
        Off
    }
}
