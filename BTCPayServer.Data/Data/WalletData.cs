using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class WalletData
    {
        [System.ComponentModel.DataAnnotations.Key]
        public string Id { get; set; }

        public List<WalletTransactionData> WalletTransactions { get; set; }

        public byte[] Blob { get; set; }
    }


    public class WalletBlobInfo
    {
        public Dictionary<string, string> LabelColors { get; set; } = new Dictionary<string, string>();
    }
}
