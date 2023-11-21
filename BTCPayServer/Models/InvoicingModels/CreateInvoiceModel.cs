using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Apps;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

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

        [DisplayName("Default payment method on checkout")]
        public string DefaultPaymentMethod
        {
            get; set;
        }
        [DisplayName("Metadata")]
        public string Metadata
        {
            get; set;
        }

        [MailboxAddress]
        [DisplayName("Buyer Email")]
        public string BuyerEmail
        {
            get; set;
        }

        [Uri]
        [DisplayName("Notification URL")]
        public string NotificationUrl
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

        [MailboxAddress]
        [DisplayName("Notification Email")]
        public string NotificationEmail
        {
            get; set;
        }

        [DisplayName("Require Refund Email")]
        public RequiresRefundEmail RequiresRefundEmail
        {
            get; set;
        }

        public CheckoutType CheckoutType { get; set; }
    }
}
