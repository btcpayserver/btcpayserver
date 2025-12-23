using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.AccountViewModels
{
    public class ForgotPasswordViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; }
    }
}
