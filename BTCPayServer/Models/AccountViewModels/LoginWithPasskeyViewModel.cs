using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.AccountViewModels
{
    /// <summary>
    /// Request model for getting passkey authentication options
    /// </summary>
    public class PasskeyLoginOptionsRequest
    {
        /// <summary>
        /// Optional email to filter credentials. If null, uses discoverable flow.
        /// </summary>
        [EmailAddress]
        public string Email { get; set; }
    }

    /// <summary>
    /// View model for completing passkey authentication
    /// </summary>
    public class LoginWithPasskeyViewModel
    {
        /// <summary>
        /// The JSON-serialized WebAuthn assertion response
        /// </summary>
        [Required]
        public string Response { get; set; }

        /// <summary>
        /// Whether to remember this browser
        /// </summary>
        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }
}
