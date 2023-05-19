using System;
using System.Collections.Generic;
using BTCPayServer.Services.Stores;

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
    public class RolesViewModel : BasePagingViewModel
    {
        public List<StoreRepository.StoreRole> Roles { get; set; } = new List<StoreRepository.StoreRole>();
        public string DefaultRole { get; set; }
        public override int CurrentPageCount => Roles.Count;
    }

}
