#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
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
        Task<AppSalesStats> GetSalesStats(AppData app, InvoiceEntity[] paidInvoices, int numberOfDays);
    }
    public interface IHasItemStatsAppType
    {
        Task<IEnumerable<AppItemStats>> GetItemStats(AppData appData, InvoiceEntity[] invoiceEntities);
    }
}
