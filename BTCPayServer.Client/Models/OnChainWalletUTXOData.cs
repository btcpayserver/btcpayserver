using System.Collections.Generic;

namespace BTCPayServer.Client.Models
{
    public class OnChainWalletUTXOData
    {
        public string Comment { get; set; }
        public decimal Amount { get; set; }
        public string Outpoint { get; set; }
        public string Link { get; set; }
    }
}
