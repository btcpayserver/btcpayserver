﻿using BTCPayServer.Services.Invoices;
using BTCPayServer.Validations;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoreViewModel
    {
        public class DerivationScheme
        {
            public string Crypto { get; set; }
            public string Value { get; set; }
        }
        class Format
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
        public StoreViewModel()
        {

        }

        public string Id { get; set; }
        [Display(Name = "Store Name")]
        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        public string StoreName
        {
            get; set;
        }

        [Url]
        [Display(Name = "Store Website")]
        [MaxLength(500)]
        public string StoreWebsite
        {
            get;
            set;
        }

        public List<StoreViewModel.DerivationScheme> DerivationSchemes { get; set; } = new List<StoreViewModel.DerivationScheme>();

        [Display(Name = "Preferred price source (eg. bitfinex, bitstamp...)")]
        public string PreferredExchange { get; set; }

        public string RateSource
        {
            get
            {
                return PreferredExchange.IsCoinAverage() ? "https://apiv2.bitcoinaverage.com/indices/global/ticker/short" : $"https://apiv2.bitcoinaverage.com/exchanges/{PreferredExchange}";
            }
        }

        [Display(Name = "Multiply the original rate by ...")]
        [Range(0.01, 10.0)]
        public double RateMultiplier
        {
            get;
            set;
        }

        [Display(Name = "Invoice expires if the full amount has not been paid after ... minutes")]
        [Range(1, 60 * 24 * 31)]
        public int InvoiceExpiration
        {
            get;
            set;
        }

        [Display(Name = "Payment invalid if transactions fails to confirm ... minutes after invoice expiration")]
        [Range(10, 60 * 24 * 31)]
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

        [Display(Name = "Add network fee to invoice (vary with mining fees)")]
        public bool NetworkFee
        {
            get; set;
        }

        public string StatusMessage
        {
            get; set;
        }
        public SelectList CryptoCurrencies { get; set; }

        [Display(Name = "Default crypto currency on checkout")]
        public string DefaultCryptoCurrency { get; set; }

        public class LightningNode
        {
            public string CryptoCode { get; set; }
            public string Address { get; set; }
        }
        public List<LightningNode> LightningNodes
        {
            get; set;
        } = new List<LightningNode>();

        public void SetCryptoCurrencies(ExplorerClientProvider explorerProvider, string defaultCrypto)
        {
            var choices = explorerProvider.GetAll().Select(o => new Format() { Name = o.Item1.CryptoCode, Value = o.Item1.CryptoCode }).ToArray();
            var chosen = choices.FirstOrDefault(f => f.Name == defaultCrypto) ?? choices.FirstOrDefault();
            CryptoCurrencies = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen);
            DefaultCryptoCurrency = chosen.Name;
        }
    }
}
