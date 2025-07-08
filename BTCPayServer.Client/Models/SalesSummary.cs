namespace BTCPayServer.Client.Models
{
    public class SalesSummary
    {
        public decimal GrossSales { get; set; } = 0m;
        public decimal NetSales { get; set; } = 0m;
        public long SalesCount { get; set; } = 0;
        public int Refunds { get; set; } = 0;
        public decimal AverageSale { get; set; } = 0m;
        public decimal Discounts { get; set; } = 0m;
        public decimal Taxes { get; set; } = 0m;
        public decimal Tips { get; set; } = 0m;
        public string Currency { get; set; } = "USD";
    }
}
