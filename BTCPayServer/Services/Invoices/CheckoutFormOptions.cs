using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Services.Invoices;

public enum CheckoutFormOptions
{
    [Display(Name = "Inherit from store settings")]
    InheritFromStore,
    
    [Display(Name = "Do not request any information")]
    None,
    
    [Display(Name = "Request email address only")]
    Email,
    
    [Display(Name = "Request shipping address")]
    Address
}
