using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTReadyViewModel
    {
        public string PSBT { get; set; }
        public string SigningKey { get; set; }
        public string SigningKeyPath { get; set; }
        public string GlobalError { get; set; }

        public class DestinationViewModel
        {
            public bool Positive { get; set; }
            public string Destination { get; set; }
            public string Balance { get; set; }
        }

        public class InputViewModel
        {
            public int Index { get; set; }
            public string Error { get; set; }
            public bool Positive { get; set; }
            public string BalanceChange { get; set; }
        }
        public bool HasErrors => Inputs.Count == 0 || Inputs.Any(i => !string.IsNullOrEmpty(i.Error));
        public string BalanceChange { get; set; }
        public bool CanCalculateBalance { get; set; }
        public bool Positive { get; set; }
        public List<DestinationViewModel> Destinations { get; set; } = new List<DestinationViewModel>();
        public List<InputViewModel> Inputs { get; set; } = new List<InputViewModel>();
        public string FeeRate { get; set; }

        internal void SetErrors(IList<PSBTError> errors)
        {
            foreach (var err in errors)
            {
                Inputs[(int)err.InputIndex].Error = err.Message;
            }
        }
    }
}
