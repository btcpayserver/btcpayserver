using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using NBXplorer.Models;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSendModel
    {
    
        public List<TransactionOutput> Outputs { get; set; } = new List<TransactionOutput>();

        public class TransactionOutput
        {
            [Display(Name = "Destination Address")]
            [Required]
            public string DestinationAddress { get; set; }

            [Display(Name = "Amount")]
            [Required]
            [Range(1E-08, 21E6)]
            public decimal? Amount { get; set; }

            [Display(Name = "Subtract fees from amount")]
            public bool SubtractFeesFromOutput { get; set; }
        }
        public decimal CurrentBalance { get; set; }

        public string CryptoCode { get; set; }

        public int RecommendedSatoshiPerByte { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Fee rate (satoshi per byte)")]
        [Required]
        public int FeeSatoshiPerByte { get; set; }

        [Display(Name = "Make sure no change UTXO is created")]
        public bool NoChange { get; set; }
        public decimal? Rate { get; set; }
        public int Divisibility { get; set; }
        public string Fiat { get; set; }
        public string RateError { get; set; }
        public bool SupportRBF { get; set; }
        [Display(Name = "Disable RBF")]
        public bool DisableRBF { get; set; }

        public bool NBXSeedAvailable { get; set; }
        public bool InputSelection { get; set; }
        public InputSelectionOption[] InputsAvailable { get; set; }
        
        [Display(Name = "UTXOs to spend from")]
        public IEnumerable<string> SelectedInputs { get; set; }

        public class InputSelectionOption
        {
            public IEnumerable<Label> Labels { get; set; }
            public string Comment { get; set; }
            public decimal Amount  { get; set; }
            public string Outpoint { get; set; }
            public string Link { get; set; }
        }
    }
}
