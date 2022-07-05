namespace BTCPayServer.Client.Models
{
    public class StoreRateConfiguration
    {
        public decimal Spread { get; set; }
        public string PreferredSource { get; set; }

        public string Script { get; set; }

        public bool UseScript { get; set; }
    }
}
