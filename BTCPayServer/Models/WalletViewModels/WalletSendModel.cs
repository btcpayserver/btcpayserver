using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Labels;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSendModel : IHasBackAndReturnUrl
    {
        public enum ThreeStateBool
        {
            Maybe,
            Yes,
            No
        }
        public class FeeRateOption
        {
            public TimeSpan Target { get; set; }
            public decimal FeeRate { get; set; }
        }
        public List<TransactionOutput> Outputs { get; set; } = new();
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

            public string PayoutId { get; set; }

            public string[] Labels { get; set; } = Array.Empty<string>();
        }
        public decimal CurrentBalance { get; set; }
        public decimal ImmatureBalance { get; set; }

        public string CryptoCode { get; set; }

        public List<FeeRateOption> RecommendedSatoshiPerByte { get; set; }

        [Display(Name = "Fee rate (sat/vB)")]
        [Required]
        public decimal? FeeSatoshiPerByte { get; set; }

        [Display(Name = "Don't create UTXO change")]
        public bool NoChange { get; set; }
        public decimal? Rate { get; set; }
        public int FiatDivisibility { get; set; }
        public int CryptoDivisibility { get; set; }
        public string Fiat { get; set; }
        public string RateError { get; set; }
        [Display(Name = "Always include non-witness UTXO if available")]
        public bool AlwaysIncludeNonWitnessUTXO { get; set; }

        public bool NBXSeedAvailable { get; set; }
        [Display(Name = "PayJoin BIP21")]
        public string PayJoinBIP21 { get; set; }
        public bool InputSelection { get; set; }
        public InputSelectionOption[] InputsAvailable { get; set; }

        [Display(Name = "UTXOs to spend from")]
        public IEnumerable<string> SelectedInputs { get; set; }

        public string BackUrl { get; set; }
        public string ReturnUrl { get; set; }
        public bool IsMultiSigOnServer { get; set; }

        public class InputSelectionOption
        {
            public IEnumerable<TransactionTagModel> Labels { get; set; }
            public string Comment { get; set; }
            public decimal Amount { get; set; }
            public string Outpoint { get; set; }
            public string Link { get; set; }
            public long Confirmations { get; set; }
        }
    }
}
