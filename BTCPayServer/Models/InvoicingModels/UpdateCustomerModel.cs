using System.ComponentModel.DataAnnotations;
using BTCPayServer.Validation;

namespace BTCPayServer.Models.InvoicingModels
{
    public class UpdateCustomerModel
    {
        [MailboxAddress]
        [Required]
        public string Email
        {
            get; set;
        }
    }
}
