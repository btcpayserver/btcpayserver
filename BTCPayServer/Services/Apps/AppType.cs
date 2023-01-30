using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Services.Apps
{
    public enum AppType
    {
        [Display(Name = "Point of Sale")]
        PointOfSale,
        Crowdfund
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
