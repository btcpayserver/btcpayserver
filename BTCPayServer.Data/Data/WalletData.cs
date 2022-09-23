using System.Collections.Generic;

namespace BTCPayServer.Data
{
    public class WalletData
    {
        [System.ComponentModel.DataAnnotations.Key]
        public string Id { get; set; }

        public List<WalletTransactionData> WalletTransactions { get; set; }

        public byte[] Blob { get; set; }
        public List<WalletScriptData> WalletScripts { get; set; }
        public IEnumerable<WalletLabelData>? WalletLabels { get; set; }
    }


    public class WalletBlobInfo
    {
        public Dictionary<string, string> LabelColors { get; set; } = new Dictionary<string, string>();
    }
}
