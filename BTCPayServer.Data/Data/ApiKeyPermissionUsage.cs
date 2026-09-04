using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Data
{
    public class ApiKeyPermissionUsage
    {
        [Key]
        public string Id { get; set; } // Id in the format [apiKeyId]-[permission]
        public string ApiKeyId { get; set; }
        public string Permission { get; set; }
        public DateTimeOffset LastUsed { get; set; }
        public int UsageCount { get; set; }
    }
}
