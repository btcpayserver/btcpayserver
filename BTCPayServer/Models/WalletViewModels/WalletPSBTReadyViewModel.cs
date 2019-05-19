using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTReadyViewModel
    {
        public class FinalizeError
        {
            public int Index { get; set; }
            public string Error { get; set; }
        }
        public string PSBT { get; set; }
        public string SigningKey { get; set; }
        public string SigningKeyPath { get; set; }
        public string GlobalError { get; set; }
        public List<FinalizeError> Errors { get; set; }

        public class DestinationViewModel
        {
            public bool Positive { get; set; }
            public string Destination { get; set; }
            public string Balance { get; set; }
        }

        public string BalanceChange { get; set; }
        public bool CanCalculateBalance { get; set; }
        public bool Positive { get; set; }
        public List<DestinationViewModel> Destinations { get; set; } = new List<DestinationViewModel>();
        public string FeeRate { get; set; }

        internal void SetErrors(IList<PSBTError> errors)
        {
            Errors = new List<FinalizeError>();
            foreach (var err in errors)
            {
                Errors.Add(new FinalizeError() { Index = (int)err.InputIndex, Error = err.Message });
            }
        }
    }
}
