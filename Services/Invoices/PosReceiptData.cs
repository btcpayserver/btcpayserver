using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Plugins.PointOfSale;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Invoices;

[JsonObject(ItemNullValueHandling  = NullValueHandling.Ignore)]
public class PosReceiptData
{
    public string Description { get; set; }
    public string Title { get; set; }
    public Dictionary<string, string> Cart { get; set; }
    public string Subtotal { get; set; }
    public string Discount { get; set; }
    public string Tip { get; set; }
    public string Total { get; set; }
    public string ItemsTotal { get; set; }
    public string Tax { get; set; }

    void UpdateTotals(PosAppData appData, PoSOrder order, PoSOrder.OrderSummary summary, string currency, DisplayFormatter displayFormatter)
    {
        Subtotal = displayFormatter.Currency(summary.PriceTaxExcluded, currency, DisplayFormatter.CurrencyFormat.Symbol);

        if (summary.Discount > 0)
        {
            var discountFormatted = displayFormatter.Currency(summary.Discount, currency, DisplayFormatter.CurrencyFormat.Symbol);
            Discount = appData.DiscountPercentage > 0 ? $"{discountFormatted} ({appData.DiscountPercentage}%)" : discountFormatted;
        }

        if (summary.Tip > 0)
        {
            var tipFormatted = displayFormatter.Currency(summary.Tip, currency, DisplayFormatter.CurrencyFormat.Symbol);
            Tip = appData.TipPercentage > 0 ? $"{tipFormatted} ({appData.TipPercentage}%)" : tipFormatted;
        }

        if (summary.Tax > 0)
        {
            var taxFormatted = displayFormatter.Currency(summary.Tax, currency, DisplayFormatter.CurrencyFormat.Symbol);
            if (order.GetTaxRate() is { } r)
                taxFormatted = $"{taxFormatted} ({r:0.######}%)";
            Tax = taxFormatted;
        }

        if (summary.ItemsTotal > 0)
        {
            var itemsTotal = displayFormatter.Currency(summary.ItemsTotal, currency, DisplayFormatter.CurrencyFormat.Symbol);
            ItemsTotal = itemsTotal;
        }

        Total = displayFormatter.Currency(summary.PriceTaxIncludedWithTips, currency, DisplayFormatter.CurrencyFormat.Symbol);
        if (ItemsTotal == Subtotal)
            ItemsTotal = null;
        if (Subtotal == Total)
            Subtotal = null;
    }

    void UpdateFromCart(IEnumerable<AppItem> appItems, PosAppData jposData, string currency, DisplayFormatter displayFormatter)
    {
        Dictionary<string,string> cartData = new();
        foreach (var cartItem in jposData.Cart ?? [])
        {
            var selectedChoice = appItems.FirstOrDefault(item => item.Id == cartItem.Id);
            if (selectedChoice is null)
                continue;
            if (jposData.Cart.Length == 1)
            {
                Title = selectedChoice.Title;
                if (!string.IsNullOrEmpty(selectedChoice.Description))
                    Description = selectedChoice.Description;
            }

            var singlePrice = displayFormatter.Currency(cartItem.Price, currency, DisplayFormatter.CurrencyFormat.Symbol);
            var totalPrice = displayFormatter.Currency(cartItem.Price * cartItem.Count, currency, DisplayFormatter.CurrencyFormat.Symbol);
            var ident = selectedChoice.Title ?? selectedChoice.Id;
            var key = selectedChoice.PriceType == AppItemPriceType.Fixed ? ident : $"{ident} ({singlePrice})";
            cartData.Add(key, $"{cartItem.Count} x {singlePrice} = {totalPrice}");
        }

        for (var i = 0; i < (jposData.Amounts ?? []).Length; i++)
        {
            cartData.Add($"Custom Amount {i + 1}", displayFormatter.Currency(jposData.Amounts[i], currency, DisplayFormatter.CurrencyFormat.Symbol));
        }

        Cart = cartData.Count > 0 ? cartData : null;
    }

    public static PosReceiptData Create(bool isTopup, IEnumerable<AppItem> choices, PosAppData jposData, PoSOrder order, PoSOrder.OrderSummary summary, string currency, DisplayFormatter displayFormatter)
    {
        var receiptData = new PosReceiptData();
        if (!isTopup)
        {
            jposData.UpdateFrom(summary);
            receiptData.UpdateTotals(jposData, order, summary, currency, displayFormatter);
        }
        receiptData.UpdateFromCart(choices, jposData, currency, displayFormatter);
        return receiptData;
    }
}
