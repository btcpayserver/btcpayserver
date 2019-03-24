using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using BTCPayServer.Data;
using BTCPayServer.Storage.Models;

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
        
        public List<StoredFile> StoredFiles
        {
            get;
            set;
        }
    }
}
