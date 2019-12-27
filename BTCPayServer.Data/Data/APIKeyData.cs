using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Data
{
    public class APIKeyData
    {
        [MaxLength(50)]
        public string Id
        {
            get; set;
        }

        [MaxLength(50)]
        public string StoreId
        {
            get; set;
        }

        public StoreData StoreData { get; set; }
    }
}
