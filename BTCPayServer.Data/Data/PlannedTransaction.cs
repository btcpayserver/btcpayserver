using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Data
{
    public class PlannedTransaction
    {
        [Key]
        [MaxLength(100)]
        public string Id { get; set; } // Id in the format [cryptocode]-[txid]
        public DateTimeOffset BroadcastAt { get; set; }
        public byte[] Blob { get; set; }
    }
}
