using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Models.ManageViewModels
{
    public class IndexViewModel
    {
        [Required]
        [EmailAddress]
        [MaxLength(50)]
        [Display(Name = "Email")]
        public string Email { get; set; }
        public bool EmailConfirmed { get; set; }
        public bool RequiresEmailConfirmation { get; set; }
        [Display(Name = "Name")]
        public string Name { get; set; }

        [Display(Name = "Profile Picture")]
        public IFormFile ImageFile { get; set; }
        public string ImageUrl { get; set; }
    }
}
