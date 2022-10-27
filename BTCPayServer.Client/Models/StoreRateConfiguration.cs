namespace BTCPayServer.Client.Models
{
    public class StoreRateConfiguration
    {
        public decimal Spread { get; set; }
        public bool IsCustomScript { get; set; }
        public string EffectiveScript { get; set; }
        public string PreferredSource { get; set; }
    }
}
