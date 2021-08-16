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
        [Display(Name = "Item list only")]
        Static,
        [Display(Name = "Item list and cart")]
        Cart,
        [Display(Name = "Keypad only")]
        Light,
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
