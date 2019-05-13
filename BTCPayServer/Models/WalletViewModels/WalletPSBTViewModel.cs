using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTViewModel
    {
        public string Decoded { get; set; }
        string _FileName;
        public string FileName
        {
            get
            {
                return string.IsNullOrEmpty(_FileName) ? "psbt-export.psbt" : _FileName;
            }
            set
            {
                _FileName = value;
            }
        }
        public string PSBT { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        internal PSBT GetPSBT(Network network)
        {
            try
            {
                return NBitcoin.PSBT.Parse(PSBT, network);
            }
            catch { }
            return null;
        }
    }
}
