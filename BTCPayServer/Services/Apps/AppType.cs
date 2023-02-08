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
    }
    
    public static class AppTypes
    {
        public const  string PointOfSale = nameof(PointOfSale);
        public const  string Crowdfund = nameof(Crowdfund);
    }

    public enum PosViewType
    {
        [Display(Name = "Product list")]
        Static,
        [Display(Name = "Product list with cart")]
        Cart,
        [Display(Name = "Keypad only")]
        Light,
        [Display(Name = "Print display")]
        Print
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
