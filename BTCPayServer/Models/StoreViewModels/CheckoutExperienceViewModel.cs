using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class CheckoutExperienceViewModel
    {
        class Format
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
        public SelectList CryptoCurrencies { get; set; }
        public SelectList Languages { get; set; }

        [Display(Name = "Default crypto currency on checkout")]
        public string DefaultCryptoCurrency { get; set; }
        [Display(Name = "Default language on checkout")]
        public string DefaultLang { get; set; }
        [Display(Name = "Allow conversion through third party (Shapeshift, Changelly...)")]
        public bool AllowCoinConversion
        {
            get; set;
        }
        [Display(Name = "Do not propose lightning payment if value of the invoice is above...")]
        [MaxLength(20)]
        public string LightningMaxValue { get; set; }

        [Display(Name = "Link to a custom CSS stylesheet")]
        [Url]
        public Uri CustomCSS { get; set; }
        [Display(Name = "Link to a custom logo")]
        [Url]
        public Uri CustomLogo { get; set; }
        public void SetCryptoCurrencies(ExplorerClientProvider explorerProvider, string defaultCrypto)
        {
            var choices = explorerProvider.GetAll().Select(o => new Format() { Name = o.Item1.CryptoCode, Value = o.Item1.CryptoCode }).ToArray();
            var chosen = choices.FirstOrDefault(f => f.Value == defaultCrypto) ?? choices.FirstOrDefault();
            CryptoCurrencies = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
            DefaultCryptoCurrency = chosen.Name;
        }

        public void SetLanguages(LanguageService langService, string defaultLang)
        {
            defaultLang = defaultLang ?? "en-US";
            var choices = langService.GetLanguages().Select(o => new Format() { Name = o.DisplayName, Value = o.Code }).ToArray();
            var chosen = choices.FirstOrDefault(f => f.Value == defaultLang) ?? choices.FirstOrDefault();
            Languages = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
            DefaultLang = chosen.Value;
        }
    }
}
