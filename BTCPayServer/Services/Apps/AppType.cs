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
        Static,
        Cart,
        Light
    }
}
