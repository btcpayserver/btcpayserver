using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.AccountViewModels
{
    public class LoginViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }
        public string LoginCode { get; set; }

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
