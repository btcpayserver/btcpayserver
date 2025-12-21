using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.PayButton.Models
{
    public class PayButtonViewModel
    {
        [ModelBinder(BinderType = typeof(InvariantDecimalModelBinder))]
        public decimal? Price { get; set; }
        public string InvoiceId { get; set; }
        public string Currency { get; set; }
        public string DefaultPaymentMethod { get; set; }
        public PaymentMethodOptionViewModel.Format[] PaymentMethods { get; set; }
        public string CheckoutDesc { get; set; }
        public string OrderId { get; set; }
        public int ButtonSize { get; set; }
        public int ButtonType { get; set; }

        // Slider properties (ButtonType = 2)
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public string Step { get; set; }

        // Custom Amount properties (ButtonType = 1)
        public bool SimpleInput { get; set; }
        public bool FitButtonInline { get; set; }

        [Url]
        public string ServerIpn { get; set; }
        [Url]
        public string BrowserRedirect { get; set; }
        [MailboxAddress]
        public string NotifyEmail { get; set; }

        public string StoreId { get; set; }
        public string CheckoutQueryString { get; set; }

        // Data that influences Pay Button UI, but not invoice creation
        public string UrlRoot { get; set; }
        public List<string> CurrencyDropdown { get; set; }
        public string PayButtonImageUrl { get; set; }
        public string PayButtonText { get; set; }
        public bool UseModal { get; set; }
        public bool JsonResponse { get; set; }
        public ListAppsViewModel.ListAppViewModel[] Apps { get; set; }
        public string AppIdEndpoint { get; set; } = "";
        public string AppChoiceKey { get; set; } = "";
    }
}
