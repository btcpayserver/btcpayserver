using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class DerivationSchemeViewModel
    {
        class Format
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
        public DerivationSchemeViewModel()
        {
            var btcPay = new Format { Name = "BTCPay", Value = "BTCPay" };
            DerivationSchemeFormat = btcPay.Value;
            DerivationSchemeFormats = new SelectList(new Format[]
            {
                btcPay,
                new Format { Name = "Electrum", Value = "Electrum" },
            }, nameof(btcPay.Value), nameof(btcPay.Name), btcPay);
        }
        public string DerivationScheme
        {
            get; set;
        }

        public List<(string KeyPath, string Address)> AddressSamples
        {
            get; set;
        } = new List<(string KeyPath, string Address)>();

        [Display(Name = "Derivation Scheme format")]
        public string DerivationSchemeFormat
        {
            get;
            set;
        }

        public string CryptoCode { get; set; }
        public bool Confirmation { get; set; }

        public SelectList DerivationSchemeFormats { get; set; }

        public string ServerUrl { get; set; }
    }
}
