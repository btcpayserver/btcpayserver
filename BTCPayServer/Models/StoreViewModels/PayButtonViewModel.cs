using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.ModelBinders;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Models.StoreViewModels
{
    public class PayButtonViewModel
    {
        [ModelBinder(BinderType = typeof(InvariantDecimalModelBinder))]
        public decimal Price { get; set; }
        public string InvoiceId { get; set; }
        [Required]
        public string Currency { get; set; }
        public string CheckoutDesc { get; set; }
        public string OrderId { get; set; }
        public int ButtonSize { get; set; }
        public int ButtonType { get; set; }

        // Slider properties (ButtonType = 2)
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public decimal Step { get; set; }

        // Custom Amount properties (ButtonType = 1)
        public bool SimpleInput { get; set; }
        public bool FitButtonInline { get; set; }

        [Url]
        public string ServerIpn { get; set; }
        [Url]
        public string BrowserRedirect { get; set; }
        [EmailAddress]
        public string NotifyEmail { get; set; }

        public string StoreId { get; set; }
        public string CheckoutQueryString { get; set; }

        // Data that influences Pay Button UI, but not invoice creation
        public string UrlRoot { get; set; }
        public List<string> CurrencyDropdown { get; set; }
        public string PayButtonImageUrl { get; set; }
    }
}
