using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Services.Apps
{
    public interface IApp
    {
        public string Description { get;  }
        public string Type { get; }
        Task<string> ConfigureLink(string appId);
        Task<string> ViewLink(AppData app);
        Task SetDefaultSettings(AppData appData, string defaultCurrency);
        Task<SalesStats> GetSaleStates(AppData app, InvoiceEntity[] paidInvoices, int numberOfDays);
        Task<IEnumerable<ItemStats>> GetItemStats(AppData appData, InvoiceEntity[] invoiceEntities);
        Task<object> GetInfo(AppData appData);
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
