using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSendModel
    {
        [Required]
        public string Destination { get; set; }

        [Range(0.0, double.MaxValue)]
        [Required]
        public decimal? Amount { get; set; }

        public decimal CurrentBalance { get; set; }

        public string CryptoCode { get; set; }

        public int RecommendedSatoshiPerByte { get; set; }

        [Display(Name = "Subtract fees from amount")]
        public bool SubstractFees { get; set; }

        [Range(1, int.MaxValue)]
        [Display(Name = "Fee rate (satoshi per byte)")]
        [Required]
        public int FeeSatoshiPerByte { get; set; }
        public decimal? Rate { get; set; }
        public int Divisibility { get; set; }
        public string Fiat { get; set; }
        public string RateError { get; set; }
    }
}
