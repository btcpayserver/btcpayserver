using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.ServerViewModels
{
    public class LightningWalletServices
    {
        public string ServiceLink { get; set; }
        public bool ShowQR { get; set; }
        public string WalletName { get; internal set; }
    }
}
