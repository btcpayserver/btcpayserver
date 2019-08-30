using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

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
