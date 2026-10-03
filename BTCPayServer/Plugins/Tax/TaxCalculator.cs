using System;

namespace BTCPayServer.Plugins.Tax;

public static class TaxCalculator
{
    public readonly record struct LineResult(decimal Tax, decimal PriceTaxExcluded, decimal PriceTaxIncluded);

    public static LineResult Calculate(decimal price, decimal taxRate, bool taxIncluded, int decimals)
    {
        if (taxRate <= 0)
            return new LineResult(0m, Round(price, decimals), Round(price, decimals));

        decimal tax;
        decimal priceExcluded;
        if (taxIncluded)
        {
            tax = Round(price * taxRate / (100.0m + taxRate), decimals);
            priceExcluded = price - tax;
        }
        else
        {
            tax = Round(price * taxRate / 100.0m, decimals);
            priceExcluded = price;
        }
        var priceIncluded = Round(priceExcluded + tax, decimals);
        return new LineResult(tax, Round(priceExcluded, decimals), priceIncluded);
    }

    public static decimal Round(decimal value, int decimals) => Math.Round(value, decimals, MidpointRounding.AwayFromZero);
}
