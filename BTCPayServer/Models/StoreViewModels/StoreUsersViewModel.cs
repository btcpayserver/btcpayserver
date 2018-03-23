using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

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
        public StoreUsersViewModel()
        {
            Role = StoreRoles.Guest;
        }
        [Required]
        [EmailAddress]
        public string Email { get; set; }
        public string StoreId { get; set; }
        public string Role { get; set; }
        public List<StoreUserViewModel> Users { get; set; }
    }
}
