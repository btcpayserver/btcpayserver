using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class CheckoutExperienceViewModel
    {
        public class Format
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public PaymentMethodId PaymentId { get; set; }
        }
        public SelectList CryptoCurrencies { get; set; }
        public SelectList Languages { get; set; }

        [Display(Name = "Default payment method on checkout")]
        public string DefaultPaymentMethod { get; set; }
        [Display(Name = "Default language on checkout")]
        public string DefaultLang { get; set; }

        [Display(Name = "Link to a custom CSS stylesheet")]
        public string CustomCSS { get; set; }
        [Display(Name = "Link to a custom logo")]
        public string CustomLogo { get; set; }

        [Display(Name = "Custom HTML title to display on Checkout page")]
        public string HtmlTitle { get; set; }

        [Display(Name = "Requires a refund email")]
        public bool RequiresRefundEmail { get; set; }

        [Display(Name = "Show recommended fee")]
        public bool ShowRecommendedFee { get; set; }

        [Display(Name = "Recommended fee confirmation target blocks")]
        [Range(1, double.PositiveInfinity)]
        public int RecommendedFeeBlockTarget { get; set; }

        [Display(Name = "Do not propose on chain payment if the value of the invoice is below...")]
        [MaxLength(20)]
        public string OnChainMinValue { get; set; }

        [Display(Name = "Do not propose lightning payment if value of the invoice is above...")]
        [MaxLength(20)]
        public string LightningMaxValue { get; set; }

        [Display(Name = "Display lightning payment amounts in Satoshis")]
        public bool LightningAmountInSatoshi { get; set; }
        
        [Display(Name = "Redirect invoice to redirect url automatically after paid")]
        public bool  RedirectAutomatically { get; set; }

        public void SetLanguages(LanguageService langService, string defaultLang)
        {
            defaultLang = langService.GetLanguages().Any(language => language.Code == defaultLang) ? defaultLang : "en";
            var choices = langService.GetLanguages().Select(o => new Format() { Name = o.DisplayName, Value = o.Code }).ToArray();
            var chosen = choices.FirstOrDefault(f => f.Value == defaultLang) ?? choices.FirstOrDefault();
            Languages = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
            DefaultLang = chosen.Value;
        }
    }
}
