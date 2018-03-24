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
        public DerivationSchemeViewModel()
        {
        }
        public string DerivationScheme
        {
            get; set;
        }

        public List<(string KeyPath, string Address)> AddressSamples
        {
            get; set;
        } = new List<(string KeyPath, string Address)>();

        public string CryptoCode { get; set; }
        [Display(Name = "Hint address")]
        public string HintAddress { get; set; }
        public bool Confirmation { get; set; }

        public string ServerUrl { get; set; }
        public string StatusMessage { get; internal set; }
    }
}
