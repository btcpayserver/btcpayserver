using System;
using System.Collections.Generic;
using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public abstract class CustodianAccountBaseData
    {
        public string CustodianCode { get; set; }
        
        public string StoreId { get; set; }
        
        public JObject Config { get; set; }
    }

}
