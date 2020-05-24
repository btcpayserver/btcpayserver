using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class SigningContextModel
    {
        public SigningContextModel()
        {

        }
        public SigningContextModel(PSBT psbt)
        {
            PSBT = psbt.ToBase64();
        }
        public string PSBT { get; set; }
        public string OriginalPSBT { get; set; }
        public string PayJoinEndpointUrl { get; set; }
        public bool? EnforceLowR { get; set; }
        public string ChangeAddress { get; set; }
    }
}
