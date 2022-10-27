using System;
using System.Collections.Generic;

namespace BTCPayServer.Data
{
    [Obsolete]
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
