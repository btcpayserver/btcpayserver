using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.Models.InvoicingModels
{
    public class CreateInvoiceModel
    {
        public decimal? Amount
        {
            get; set;
        }
        public string Currency
        {
            get; set;
        }

        [Required]
        [DisplayName("Store Id")]
        public string StoreId
        {
            get; set;
        }

        [DisplayName("Order Id")]
        public string OrderId
        {
            get; set;
        }

        [DisplayName("Item Description")]
        public string ItemDesc
        {
            get; set;
        }

        [Display(Name = "Default payment method on checkout")]
        public string DefaultPaymentMethod
        {
            get; set;
        }

        [DisplayName("POS Data")]
        public string PosData
        {
            get; set;
        }

        [EmailAddress]
        [DisplayName("Buyer Email")]
        public string BuyerEmail
        {
            get; set;
        }
        
        [DisplayName("Buyer Name")]
        public string BuyerName
        {
            get; set;
        }
        
        [DisplayName("Buyer Phone")]
        public string BuyerPhone
        {
            get; set;
        }
        
        [DisplayName("Buyer Address 1")]
        public string BuyerAddress1
        {
            get; set;
        }
        
        [DisplayName("Buyer Address 2")]
        public string BuyerAddress2
        {
            get; set;
        }
        
        [DisplayName("Buyer City")]
        public string BuyerCity
        {
            get; set;
        }
        
        [DisplayName("Buyer State")]
        public string BuyerState
        {
            get; set;
        }
        
        [DisplayName("Buyer Country")]
        public string BuyerCountry
        {
            get; set;
        }
        
        [DisplayName("Buyer Zip")]
        public string BuyerZip
        {
            get; set;
        }

        [Uri]
        [DisplayName("Notification Url")]
        public string NotificationUrl
        {
            get; set;
        }

        [DisplayName("Store")]
        public SelectList Stores
        {
            get; set;
        }

        [DisplayName("Supported Transaction Currencies")]
        public List<string> SupportedTransactionCurrencies
        {
            get; set;
        }

        [DisplayName("Available Payment Methods")]
        public SelectList AvailablePaymentMethods
        {
            get; set;
        }

        [EmailAddress]
        [DisplayName("Notification Email")]
        public string NotificationEmail
        {
            get; set;
        }

        [Display(Name = "Require Refund Email")]
        public RequiresRefundEmail RequiresRefundEmail
        {
            get; set;
        }
    }
}
