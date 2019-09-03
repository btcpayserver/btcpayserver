﻿using BTCPayServer.Validation;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoreViewModel
    {
        public class DerivationScheme
        {
            public string Crypto { get; set; }
            public string Value { get; set; }
            public WalletId WalletId { get; set; }
            public bool Enabled { get; set; }
        }
        
        public class AdditionalPaymentMethod
        {
            public string Provider { get; set; }
            public bool Enabled { get; set; }
            public string Action { get; set; }
        }
        public StoreViewModel()
        {

        }

        public bool CanDelete { get; set; }
        public string Id { get; set; }
        [Display(Name = "Store Name")]
        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        public string StoreName
        {
            get; set;
        }

        [Uri]
        [Display(Name = "Store Website")]
        [MaxLength(500)]
        public string StoreWebsite
        {
            get;
            set;
        }

        [Display(Name = "Allow anyone to create invoice")]
        public bool AnyoneCanCreateInvoice { get; set; }

        public List<StoreViewModel.DerivationScheme> DerivationSchemes { get; set; } = new List<StoreViewModel.DerivationScheme>();

        public List<AdditionalPaymentMethod> ThirdPartyPaymentMethods { get; set; } =
            new List<AdditionalPaymentMethod>();

        [Display(Name = "Invoice expires if the full amount has not been paid after ... minutes")]
        [Range(1, 60 * 24 * 24)]
        public int InvoiceExpiration
        {
            get;
            set;
        }

        [Display(Name = "Payment invalid if transactions fails to confirm ... minutes after invoice expiration")]
        [Range(10, 60 * 24 * 24)]
        public int MonitoringExpiration
        {
            get;
            set;
        }

        [Display(Name = "Consider the invoice confirmed when the payment transaction...")]
        public SpeedPolicy SpeedPolicy
        {
            get; set;
        }

        [Display(Name = "Add additional fee (network fee) to invoice...")]
        public Data.NetworkFeeMode NetworkFeeMode
        {
            get; set;
        }

        [Display(Name = "Description template of the lightning invoice")]
        public string LightningDescriptionTemplate { get; set; }

        public class LightningNode
        {
            public string CryptoCode { get; set; }
            public string Address { get; set; }
            public bool Enabled { get; set; }
        }
        public List<LightningNode> LightningNodes
        {
            get; set;
        } = new List<LightningNode>();

        [Display(Name = "Consider the invoice paid even if the paid amount is ... % less than expected")]
        [Range(0, 100)]
        public double PaymentTolerance
        {
            get;
            set;
        }
    }
}
