using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.StoreViewModels
{
    public class CheckoutAppearanceViewModel
    {
        public SelectList PaymentMethods { get; set; }

        [Display(Name = "Unify payment methods into one payment URL/QR code")]
        public bool OnChainWithLnInvoiceFallback { get; set; }

        [Display(Name = "Show \"Pay in wallet\" button")]
        public bool ShowPayInWalletButton { get; set; }

        [Display(Name = "Show the store header")]
        public bool ShowStoreHeader { get; set; }

        [Display(Name = "Display Lightning payment amounts in Satoshis")]
        public bool LightningAmountInSatoshi { get; set; }

        [Display(Name = "Default payment method on checkout")]
        public string DefaultPaymentMethod { get; set; }

        [Display(Name = "Celebrate payment with confetti")]
        public bool CelebratePayment { get; set; }

        [Display(Name = "Enable sounds on checkout page")]
        public bool PlaySoundOnPayment { get; set; }

        [Display(Name = "Enable NFC payments on checkout (contactless cards and LNURL-withdraw)")]
        public bool NfcEnabled { get; set; }

        [Display(Name = "Only enable the payment method after user explicitly chooses it")]
        public bool LazyPaymentMethods { get; set; }

        [Display(Name = "Allow creating zero-amount invoices")]
        public bool AllowZeroAmountInvoices { get; set; }

        [Display(Name = "Redirect invoice to redirect url automatically after paid")]
        public bool RedirectAutomatically { get; set; }

        [Display(Name = "Custom sound file for successful payment")]
        public IFormFile SoundFile { get; set; }
        public string PaymentSoundUrl { get; set; }

        [Display(Name = "Custom HTML title to display on Checkout page")]
        public string HtmlTitle { get; set; }

        [Display(Name = "Support URL")]
        [MaxLength(500)]
        public string SupportUrl { get; set; }

        [Display(Name = "Custom checkout text")]
        [MaxLength(500)]
        public string CheckoutText { get; set; }

        [Display(Name = "Show a timer … minutes before invoice expiration")]
        [Range(1, 60 * 24 * 24)]
        public int DisplayExpirationTimer { get; set; }

        [Display(Name = "Public invoice link expiration")]
        [Range(0, LengthLimits.MaxExpirationDays)]
        public int? PublicInvoiceExpiration { get; set; }

        public class ReceiptOptionsViewModel
        {
            public static ReceiptOptionsViewModel Create(Client.Models.InvoiceDataBase.ReceiptOptions opts)
            {
                opts = Client.Models.InvoiceDataBase.ReceiptOptions.Merge(opts, null);
                return new ReceiptOptionsViewModel
                {
                    Enabled = opts.Enabled.Value,
                    ShowQR = opts.ShowQR.Value,
                    ShowPayments = opts.ShowPayments.Value,
                    PublicReceiptExpiration = opts.PublicReceiptExpiration is { } expiration ? (int)expiration.TotalDays : null
                };
            }
            [Display(Name = "Enable public receipt page for settled invoices")]
            public bool Enabled { get; set; }

            [Display(Name = "Show the QR code of the receipt in the public receipt page")]
            public bool ShowQR { get; set; }

            [Display(Name = "Show the payment list in the public receipt page")]
            public bool ShowPayments { get; set; }

            [Display(Name = "Public receipt link expiration")]
            [Range(0, LengthLimits.MaxExpirationDays)]
            public int? PublicReceiptExpiration { get; set; }
            public Client.Models.InvoiceDataBase.ReceiptOptions ToDTO()
            {
                return new Client.Models.InvoiceDataBase.ReceiptOptions
                {
                    Enabled = Enabled,
                    ShowQR = ShowQR,
                    ShowPayments = ShowPayments,
                    PublicReceiptExpiration = PublicReceiptExpiration is { } days ? TimeSpan.FromDays(days) : null
                };
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
    }
}
