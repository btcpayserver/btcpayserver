using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public enum PosViewType
    {
        Static,
        Cart,
        Light,
        Print
    }

    public class CreateAppRequest
    {
        public string AppName { get; set; }
        public string AppType { get; set; }
    }

    public class CreatePointOfSaleAppRequest : CreateAppRequest
    {
        public string Currency { get; set; } = null;
        public string Title { get; set; } = null;
        public string Description { get; set; } = null;
        public string Template { get; set; } = null;
        [JsonConverter(typeof(StringEnumConverter))]
        public PosViewType DefaultView { get; set; }
        public bool ShowCustomAmount { get; set; } = false;
        public bool ShowDiscount { get; set; } = true;
        public bool EnableTips { get; set; } = true;
        public string CustomAmountPayButtonText { get; set; } = null;
        public string FixedAmountPayButtonText { get; set; } = null;
        public string TipText { get; set; } = null;
        public string CustomCSSLink { get; set; } = null;
        public string NotificationUrl { get; set; } = null;
        public string RedirectUrl { get; set; } = null;
        public bool? RedirectAutomatically { get; set; } = null;
        public bool? RequiresRefundEmail { get; set; } = null;
        public string EmbeddedCSS { get; set; } = null;
    }
}
