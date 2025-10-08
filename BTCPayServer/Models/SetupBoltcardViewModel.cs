namespace BTCPayServer.Models
{
    public class SetupBoltcardViewModel
    {
        public string ReturnUrl { get; set; }
        public string BoltcardUrl { get; set; }
        public bool NewCard { get; set; }
        public string PullPaymentId { get; set; }
    }
}
