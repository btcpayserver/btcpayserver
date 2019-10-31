using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.ServerViewModels
{
    public class UsersViewModel
    {
        public class UserViewModel
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
        }
        
        public int Skip { get; set; }
        public int Count { get; set; }
        public int Total { get; set; }

        public List<UserViewModel> Users { get; set; } = new List<UserViewModel>();
    }

}
