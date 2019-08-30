using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class WalletTransactionData
    {
        public string WalletDataId { get; set; }
        public WalletData WalletData { get; set; }
        public string TransactionId { get; set; }
        public string Labels { get; set; }
        public byte[] Blob { get; set; }
    }

    public class WalletTransactionInfo
    {
        public string Comment { get; set; } = string.Empty;
        [JsonIgnore]
        public HashSet<string> Labels { get; set; } = new HashSet<string>();
    }
}
