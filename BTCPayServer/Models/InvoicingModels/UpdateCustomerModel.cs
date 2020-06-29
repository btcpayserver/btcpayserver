using System.ComponentModel.DataAnnotations;

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
