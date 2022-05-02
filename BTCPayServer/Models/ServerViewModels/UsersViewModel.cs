using System;
using System.Collections.Generic;

namespace BTCPayServer.Models.ServerViewModels
{
    public class UsersViewModel : BasePagingViewModel
    {
        public class UserViewModel
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Email { get; set; }
            public bool Verified { get; set; }
            public bool Disabled { get; set; }
            public bool IsAdmin { get; set; }
            public DateTimeOffset? Created { get; set; }
            public IEnumerable<string> Roles { get; set; }
        }
        public List<UserViewModel> Users { get; set; } = new List<UserViewModel>();
        public override int CurrentPageCount => Users.Count;
        public Dictionary<string, string> Roles { get; set; }
    }

}
