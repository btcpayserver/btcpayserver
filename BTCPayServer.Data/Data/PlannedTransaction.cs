using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Data
{
    public class PlannedTransaction
    {
        [Key]
        [MaxLength(100)]
        // Id in the format [cryptocode]-[txid]
        public string Id { get; set; }
        public DateTimeOffset BroadcastAt { get; set; }
        public byte[] Blob { get; set; }
    }
}
