using BTCPayServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class UserStore
    {
        public string ApplicationUserId
        {
            get; set;
        }
        public ApplicationUser ApplicationUser
        {
            get; set;
        }

        public string StoreDataId
        {
            get; set;
        }
        public StoreData StoreData
        {
            get; set;
        }
        public string Role
        {
            get;
            set;
        }
    }
}
