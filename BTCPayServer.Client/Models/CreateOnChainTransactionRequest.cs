using System.Collections.Generic;

namespace BTCPayServer.Client.Models
{
    public class CreateOnChainTransactionRequest
    {
        public class CreateOnChainTransactionRequestDestination
        {
            public string Destination { get; set; }
            public decimal Amount { get; set; }
            public bool SubtractFromAmount { get; set; }
        }

        public decimal? FeeSatoshiPerByte { get; set; }
        public bool ProceedWithPayjoin { get; set; }
        public bool NoChange { get; set; }
        public IEnumerable<string> SelectedInputs { get; set; }
    }
}
