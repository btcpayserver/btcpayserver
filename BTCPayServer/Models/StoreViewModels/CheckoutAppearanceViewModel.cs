using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json.Linq;
using YamlDotNet.Core.Tokens;

namespace BTCPayServer.Models.StoreViewModels
{
    public class CheckoutAppearanceViewModel
    {
        public SelectList PaymentMethods { get; set; }

        public void SetLanguages(LanguageService langService, string defaultLang)
        {
            defaultLang = langService.GetLanguages().Any(language => language.Code == defaultLang) ? defaultLang : "en";
            var choices = langService.GetLanguages().Select(o => new PaymentMethodOptionViewModel.Format() { Name = o.DisplayName, Value = o.Code }).ToArray().OrderBy(o => o.Name);
            var chosen = choices.FirstOrDefault(f => f.Value == defaultLang) ?? choices.FirstOrDefault();
            Languages = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
            DefaultLang = chosen.Value;
        }
        
        public SelectList Languages { get; set; }

        [Display(Name = "Request customer data on checkout")]
        public string CheckoutFormId { get; set; }

        [Display(Name = "Include Lightning invoice fallback to on-chain BIP21 payment URL")]
        public bool OnChainWithLnInvoiceFallback { get; set; }

        [Display(Name = "Default payment method on checkout")]
        public string DefaultPaymentMethod { get; set; }

        [Display(Name = "Use the new checkout")]
        public bool UseNewCheckout { get; set; }
        
        [Display(Name = "Requires a refund email")]
        public bool RequiresRefundEmail { get; set; }

        [Display(Name = "Only enable the payment method after user explicitly chooses it")]
        public bool LazyPaymentMethods { get; set; }

        [Display(Name = "Redirect invoice to redirect url automatically after paid")]
        public bool RedirectAutomatically { get; set; }

        [Display(Name = "Auto-detect language on checkout")]
        public bool AutoDetectLanguage { get; set; }

        [Display(Name = "Default language on checkout")]
        public string DefaultLang { get; set; }

        [Display(Name = "Link to a custom CSS stylesheet")]
        public string CustomCSS { get; set; }
        [Display(Name = "Link to a custom logo")]
        public string CustomLogo { get; set; }

        [Display(Name = "Custom HTML title to display on Checkout page")]
        public string HtmlTitle { get; set; }

        public class ReceiptOptionsViewModel
        {
            public static ReceiptOptionsViewModel Create(Client.Models.InvoiceDataBase.ReceiptOptions opts)
            {
                return JObject.FromObject(opts).ToObject<ReceiptOptionsViewModel>();
            }
            [Display(Name = "Enable public receipt page for settled invoices")]
            public bool Enabled { get; set; }
            
            [Display(Name = "Show the QR code of the receipt in the public receipt page")]
            public bool ShowQR { get; set; }

            [Display(Name = "Show the payment list in the public receipt page")]
            public bool ShowPayments { get; set; }
            public Client.Models.InvoiceDataBase.ReceiptOptions ToDTO()
            {
                return JObject.FromObject(this).ToObject<Client.Models.InvoiceDataBase.ReceiptOptions>();
            }
        }
        public ReceiptOptionsViewModel ReceiptOptions { get; set; } = ReceiptOptionsViewModel.Create(Client.Models.InvoiceDataBase.ReceiptOptions.CreateDefault());
        public List<PaymentMethodCriteriaViewModel> PaymentMethodCriteria { get; set; }
    }

    public class PaymentMethodCriteriaViewModel
    {
        public string PaymentMethod { get; set; }
        public string Value { get; set; }

        public CriteriaType Type { get; set; }

        public enum CriteriaType
        {
            GreaterThan,
            LessThan
        }
        public static string ToString(CriteriaType type)
        {
            switch (type)
            {
                case CriteriaType.GreaterThan:
                    return "Greater than";
                case CriteriaType.LessThan:
                    return "Less than";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

    }
}
