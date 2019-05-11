using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTReadyViewModel
    {
        public string PSBT { get; set; }
        public List<string> Errors { get; set; }
    }
}
