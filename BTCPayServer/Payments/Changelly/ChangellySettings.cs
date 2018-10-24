namespace BTCPayServer.Payments.Changelly
{
    public class ChangellySettings
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string ApiUrl { get; set; }
        public bool Enabled { get; set; }
        public string ChangellyMerchantId { get; set; }
        public decimal AmountMarkupPercentage { get; set; }
        public bool ShowFiat { get; set; }

        public bool IsConfigured()
        {
            return
                !string.IsNullOrEmpty(ApiKey) ||
                !string.IsNullOrEmpty(ApiSecret) ||
                !string.IsNullOrEmpty(ApiUrl);
        }
    }
}
