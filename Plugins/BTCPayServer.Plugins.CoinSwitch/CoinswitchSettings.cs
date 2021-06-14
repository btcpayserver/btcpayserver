namespace BTCPayServer.Plugins.CoinSwitch
{
    public class CoinSwitchSettings
    {
        public string MerchantId { get; set; }
        public string Mode { get; set; }
        public bool Enabled { get; set; }
        public decimal AmountMarkupPercentage { get; set; }

        public bool IsConfigured()
        {
            return
                !string.IsNullOrEmpty(MerchantId);
        }
    }
}
