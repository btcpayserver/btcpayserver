using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTReadyViewModel : IHasBackAndReturnUrl
    {
        public SigningContextModel SigningContext { get; set; } = new SigningContextModel();
        public string SigningKey { get; set; }
        public string SigningKeyPath { get; set; }

        public class DestinationViewModel
        {
            public bool Positive { get; set; }
            public string Destination { get; set; }
            public StringAmounts Balance { get; set; }
            public IEnumerable<TransactionTagModel> Labels { get; set; } = new List<TransactionTagModel>();
        }

        public class InputViewModel
        {
            public int Index { get; set; }
            public string Error { get; set; }
            public bool Positive { get; set; }
            public StringAmounts BalanceChange { get; set; }
            public IEnumerable<TransactionTagModel> Labels { get; set; } = new List<TransactionTagModel>();
        }
        public class AmountViewModel
        {
            public bool Positive { get; set; }
            public StringAmounts BalanceChange { get; set; }
        }
        public AmountViewModel ReplacementBalanceChange { get; set; }
        public bool HasErrors => Inputs.Count == 0 || Inputs.Any(i => !string.IsNullOrEmpty(i.Error));
        public StringAmounts BalanceChange { get; set; }
        public bool CanCalculateBalance { get; set; }
        public bool Positive { get; set; }
        public List<DestinationViewModel> Destinations { get; set; } = new List<DestinationViewModel>();
        public List<InputViewModel> Inputs { get; set; } = new List<InputViewModel>();
        public string FeeRate { get; set; }
        public string BackUrl { get; set; }
        public string ReturnUrl { get; set; }

        internal void SetErrors(IList<PSBTError> errors)
        {
            foreach (var err in errors)
            {
                Inputs[(int)err.InputIndex].Error = err.Message;
            }
        }

        public record StringAmounts(string CryptoAmount, string FiatAmount);
    }
}
