using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Data
{
    public class OffchainTransactionData
    {
        [Key]
        [MaxLength(32 * 2)]
        public string Id { get; set; }
        public byte[] Blob { get; set; }
    }
}
