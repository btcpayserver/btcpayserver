using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Data
{
    /// <summary>
    /// We represent the locks of the PayjoinRepository
    /// with this table. (Both, our utxo we locked as part of a payjoin
    /// and the utxo of the payer which were used to pay us)
    /// </summary>
    public class PayjoinLock
    {
        [Key]
        [MaxLength(100)]
        public string Id { get; set; }
    }
}
