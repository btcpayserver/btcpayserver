using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Validation;

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
        public string StoreId
        {
            get; set;
        }

        public string OrderId
        {
            get; set;
        }

        public string ItemDesc
        {
            get; set;
        }

        public string PosData
        {
            get; set;
        }

        [EmailAddress]
        public string BuyerEmail
        {
            get; set;
        }

        [EmailAddress]
        public string NotificationEmail
        {
            get; set;
        }

        [Uri]
        public string NotificationUrl
        {
            get; set;
        }

        public SelectList Stores
        {
            get;
            set;
        }
    }
}
