using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Plugins.PointOfSale.Models;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Invoices;

public class PosAppData
{
    [JsonProperty(PropertyName = "cart")]
    public PosAppCartItem[] Cart { get; set; }
    
    [JsonProperty(PropertyName = "customAmount")]
    public decimal CustomAmount { get; set; }
    
    [JsonProperty(PropertyName = "discountPercentage")]
    public decimal DiscountPercentage { get; set; }
    
    [JsonProperty(PropertyName = "discountAmount")]
    public decimal DiscountAmount { get; set; }
    
    [JsonProperty(PropertyName = "tip")]
    public decimal Tip { get; set; }

    [JsonProperty(PropertyName = "subTotal")]
    public decimal Subtotal { get; set; }

    [JsonProperty(PropertyName = "total")]
    public decimal Total { get; set; }
}

public class PosAppCartItem
{
    [JsonProperty(PropertyName = "id")]
    public string Id { get; set; }
    
    [JsonProperty(PropertyName = "price")]
    public PosAppCartItemPrice Price { get; set; }
    
    [JsonProperty(PropertyName = "title")]
    public string Title { get; set; }
    
    [JsonProperty(PropertyName = "count")]
    public int Count { get; set; }
    
    [JsonProperty(PropertyName = "inventory")]
    public int? Inventory { get; set; }
    
    [JsonProperty(PropertyName = "image")]
    public string Image { get; set; }
}

public class PosAppCartItemPrice
{
    [JsonProperty(PropertyName = "formatted")]
    public string Formatted { get; set; }
    
    [JsonProperty(PropertyName = "value")]
    public decimal Value { get; set; }
    
    [JsonProperty(PropertyName = "type")]
    public ViewPointOfSaleViewModel.Item.ItemPrice.ItemPriceType Type { get; set; }
}
