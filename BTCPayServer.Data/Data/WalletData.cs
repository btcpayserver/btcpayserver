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

        public string PaymentType { get; set; }
        public string CryptoCode { get; set; }

        public string ApplicationUserId { get; set; }        
        
        public List<WalletTransactionData> WalletTransactions { get; set; }

        public byte[] Blob { get; set; }
        
        public IEnumerable<StoreWalletData> StoreWalletDatas { get; set; }
        
    }
}
