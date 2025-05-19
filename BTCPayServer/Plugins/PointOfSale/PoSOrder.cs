#nullable  enable
using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.PointOfSale;

public class PoSOrder
{
    private readonly int _decimals;
    decimal _discount;
    decimal _tip;
    List<ItemLine> ItemLines = new();

    public PoSOrder(int decimals)
    {
        _decimals = decimals;
    }

    public record ItemLine(string ItemId, int Count, decimal UnitPrice, decimal TaxRate);
    public void AddLine(ItemLine line)
    {
        ItemLines.Add(line);
    }

    public class OrderSummary
    {
        public decimal Discount { get; set; }
        public decimal Tax { get; set; }
        public decimal ItemsTotal { get; set; }
        public decimal PriceTaxExcluded { get; set; }
        public decimal Tip { get; set; }
        public decimal PriceTaxIncluded { get; set; }
        public decimal PriceTaxIncludedWithTips { get; set; }
    }

    public OrderSummary Calculate()
    {
        var ctx = new OrderSummary();
        foreach (var item in ItemLines)
        {
            var linePrice = item.UnitPrice * item.Count;
            var discount = linePrice * _discount / 100.0m;
            discount = Round(discount);
            ctx.Discount += discount;
            linePrice -= discount;
            var tax = linePrice * item.TaxRate / 100.0m;
            tax =  Round(tax);
            ctx.Tax += tax;
            ctx.PriceTaxExcluded += linePrice;
        }
        ctx.PriceTaxExcluded = Round(ctx.PriceTaxExcluded);
        ctx.PriceTaxIncluded = ctx.PriceTaxExcluded + ctx.Tax;
        ctx.PriceTaxIncludedWithTips = ctx.PriceTaxIncluded + _tip;
        ctx.PriceTaxIncludedWithTips = Round(ctx.PriceTaxIncludedWithTips);
        ctx.Tip = Round(_tip);
        ctx.ItemsTotal = ctx.PriceTaxExcluded + ctx.Discount;
        return ctx;
    }

    decimal Round(decimal value) => Math.Round(value, _decimals, MidpointRounding.AwayFromZero);

    public void AddTip(decimal tip)
    {
        _tip = Round(tip);
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="discount">From 0 to 100</param>
    public void AddDiscountRate(decimal discount)
    {
        _discount = discount;
    }
}
