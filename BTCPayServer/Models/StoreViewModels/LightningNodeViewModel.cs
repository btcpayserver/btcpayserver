using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class LightningNodeViewModel
    {
        class Format
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
        [Display(Name = "Lightning charge url")]
        public string Url
        {
            get;
            set;
        }

        [Display(Name = "Crypto currency")]
        public string CryptoCurrency
        {
            get;
            set;
        }
        public SelectList CryptoCurrencies { get; set; }
        public string StatusMessage { get; set; }
        public string InternalLightningNode { get; internal set; }

        public void SetCryptoCurrencies(BTCPayNetworkProvider networkProvider, string selectedScheme)
        {
            var choices = networkProvider.GetAll()
                            .Where(n => n.CLightningNetworkName != null)
                            .Select(o => new Format() { Name = o.CryptoCode, Value = o.CryptoCode }).ToArray();
            var chosen = choices.FirstOrDefault(f => f.Name == selectedScheme) ?? choices.FirstOrDefault();
            CryptoCurrencies = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
            CryptoCurrency = chosen.Name;
        }
    }
}
