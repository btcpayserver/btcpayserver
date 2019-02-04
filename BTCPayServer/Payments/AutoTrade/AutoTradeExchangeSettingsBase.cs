namespace BTCPayServer.Payments.AutoTrade
{
    public abstract class AutoTradeExchangeSettingsBase
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string ApiUrl { get; set; }
        public bool Enabled { get; set; }

        public abstract string ExchangeName { get; }
        public string CurrencyTypeToBuy { get; }
        public decimal AmountMarkupPercentage { get; set; }

        public bool IsConfigured()
        {
            return
                !string.IsNullOrEmpty(ApiKey) ||
                !string.IsNullOrEmpty(ApiSecret) ||
                !string.IsNullOrEmpty(ApiUrl);
        }
    }
}
