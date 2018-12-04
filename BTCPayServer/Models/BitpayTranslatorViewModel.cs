using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models
{
    public class BitpayTranslatorViewModel
    {
        [Display(Name = "Bitpay's invoice URL or obsolete invoice url")]
        public string BitpayLink { get; set; }
        public string Address { get; set; }
        public string Amount { get; set; }
        public string BitcoinUri { get; set; }
    }
}
