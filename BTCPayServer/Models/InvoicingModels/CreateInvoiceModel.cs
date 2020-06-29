using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Models.InvoicingModels
{
    public class CreateInvoiceModel
    {
        public CreateInvoiceModel()
        {
            Currency = "USD";
        }

        [Required]
        public decimal? Amount
        {
            get; set;
        }

        [Required]
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

        [Uri]
        [DisplayName("Notification Url")]
        public string NotificationUrl
        {
            get; set;
        }

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
    }
}
