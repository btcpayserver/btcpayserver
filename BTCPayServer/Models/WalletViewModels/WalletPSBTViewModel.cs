using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTViewModel
    {
        public string Decoded { get; set; }
        public string PSBT { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}
