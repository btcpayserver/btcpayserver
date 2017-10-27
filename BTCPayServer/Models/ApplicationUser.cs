using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using BTCPayServer.Data;

namespace BTCPayServer.Models
{
    // Add profile data for application users by adding properties to the ApplicationUser class
    public class ApplicationUser : IdentityUser
    {
        public List<UserStore> UserStores
        {
            get;
            set;
        }

        public bool RequiresEmailConfirmation
        {
            get; set;
        }
    }
}
