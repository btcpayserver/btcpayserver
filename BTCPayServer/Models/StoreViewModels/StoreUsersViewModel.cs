using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoreUsersViewModel
    {
        public class StoreUserViewModel
        {
            [Display(Name = "Email")]
            public string Email { get; set; }
            
            [Display(Name = "Role")]
            public string Role { get; set; }
            
            [Display(Name = "Name")]
            public string Name { get; set; }
            
            public string ImageUrl { get; set; }
            public string Id { get; set; }
        }
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; }
        public string StoreId { get; set; }
        
        [Display(Name = "Role")]
        public string Role { get; set; }
        public List<StoreUserViewModel> Users { get; set; }
    }
}
