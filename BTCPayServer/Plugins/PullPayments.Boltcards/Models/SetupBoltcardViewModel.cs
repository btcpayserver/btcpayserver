using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.PullPayments.Boltcards
{
    public class SetupBoltcardViewModel
    {
        public string ReturnUrl { get; set; }
        
        [Display(Name = "Boltcard URL")]
        public string BoltcardUrl { get; set; }
        
        [Display(Name = "New Card")]
        public bool NewCard { get; set; }
        
        public string PullPaymentId { get; set; }
    }
}
