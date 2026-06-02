using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.AccountViewModels
{
    public class LoginViewModel
    {
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }
        public string LoginCode { get; set; }
        public string PasskeyResponse { get; set; }

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }

        public string Method { get; set; }
    }
}
