using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.InvoicingModels
{
    public class UpdateCustomerModel
    {
        [EmailAddress]
        [Required]
        public string Email
        {
            get; set;
        }
    }
}
