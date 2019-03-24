using System;
using System.ComponentModel.DataAnnotations.Schema;
using BTCPayServer.Models;

namespace BTCPayServer.Storage.Models
{
    public class StoredFile
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string Id { get; set; }

        public string FileName { get; set; }
        public string StorageFileName { get; set; }
        public DateTime Timestamp { get; set; }
        public string ApplicationUserId { get; set; }
        public ApplicationUser ApplicationUser
        {
            get; set;
        }
    }
}
