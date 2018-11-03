using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSendLedgerModel
    {
        public int FeeSatoshiPerByte { get; set; }
        public bool SubstractFees { get; set; }
        public decimal Amount { get; set; }
        public string Destination { get; set; }
    }
}
