using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Data
{
    public class WebhookData
    {
        [Key]
        [MaxLength(25)]
        public string Id { get; set; }
        [Required]
        public byte[] Blob { get; set; }
        public List<WebhookDeliveryData> Deliveries { get; set; }
    }
}
