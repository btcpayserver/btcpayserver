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
        string ConfigureLink(string appId);
        Task<SalesStats> GetSaleStates(AppData app, InvoiceEntity[] paidInvoices, int numberOfDays);
        Task<IEnumerable<ItemStats>> GetItemStats(AppData appData, InvoiceEntity[] invoiceEntities);
        Task<object> GetInfo(AppData appData);
        Task SetDefaultSettings(AppData appData, string defaultCurrency);
        string ViewLink(AppData app);
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
