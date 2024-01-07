using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoreUsersViewModel
    {
        public class StoreUserViewModel
        {
            public string Email { get; set; }
            public string Role { get; set; }
            public string Id { get; set; }
        }
        [Required]
        [EmailAddress]
        public string Email { get; set; }
        public string StoreId { get; set; }
        public string Role { get; set; }
        public List<StoreUserViewModel> Users { get; set; }
    }
}
