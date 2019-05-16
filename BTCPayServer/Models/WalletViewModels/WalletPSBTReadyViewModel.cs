using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletPSBTReadyViewModel
    {
        public string PSBT { get; set; }
        public string SigningKey { get; set; }
        public string SigningKeyPath { get; set; }
        public List<string> Errors { get; set; }

        public class DestinationViewModel
        {
            public bool Positive { get; set; }
            public string Destination { get; set; }
            public string Balance { get; set; }
        }

        public string BalanceChange { get; set; }
        public bool Positive { get; set; }
        public List<DestinationViewModel> Destinations { get; set; } = new List<DestinationViewModel>();
        public string Fee { get; set; }
        public string FeeRate { get; set; }
    }
}
