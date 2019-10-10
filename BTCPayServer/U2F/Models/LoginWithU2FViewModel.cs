using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.U2F.Models
{
    public class LoginWithU2FViewModel
    {
        public string UserId { get; set; }
        [Required]
        [Display(Name = "App id")]
        public string AppId { get; set; }

        [Required]
        [Display(Name = "Version")]
        public string Version { get; set; }

        [Required]
        [Display(Name = "Device Response")]
        public string DeviceResponse { get; set; }

        [Display(Name = "Challenges")]
        public List<ServerChallenge> Challenges { get; set; }

        [Display(Name = "Challenge")]
        public string Challenge { get; set; }

        public bool RememberMe { get; set; }
    }
}
