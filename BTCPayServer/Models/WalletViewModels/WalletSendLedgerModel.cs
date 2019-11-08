using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSendLedgerModel
    {
        public string WebsocketPath { get; set; }
        public string PSBT { get; set; }
        public string HintChange { get; set; }
    }
}
