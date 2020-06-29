using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.ServerViewModels
{
    public class UserViewModel
    {
        public string Id { get; set; }
        public string Email { get; set; }
        [Display(Name = "Is admin")]
        public bool IsAdmin { get; set; }
    }
}
