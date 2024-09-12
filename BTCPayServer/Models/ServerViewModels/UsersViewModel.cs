using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Models.ServerViewModels
{
    public class UsersViewModel : BasePagingViewModel
    {
        public class UserViewModel
        {
            public string Id { get; set; }
            public string Email { get; set; }
            public string Name { get; set; }
            [Display(Name = "Invitation URL")]
            public string InvitationUrl { get; set; }
            [Display(Name = "Image")]
            public IFormFile ImageFile { get; set; }
            public string ImageUrl { get; set; }
            public bool? EmailConfirmed { get; set; }
            public bool? Approved { get; set; }
            public bool Disabled { get; set; }
            public bool IsAdmin { get; set; }
            public DateTimeOffset? Created { get; set; }
            public IEnumerable<string> Roles { get; set; }
            public IEnumerable<UserStore> Stores { get; set; }
        }
        public List<UserViewModel> Users { get; set; } = [];
        public override int CurrentPageCount => Users.Count;
        public Dictionary<string, string> Roles { get; set; }
    }
    public class RolesViewModel : BasePagingViewModel
    {
        public List<StoreRepository.StoreRole> Roles { get; set; } = [];
        public string DefaultRole { get; set; }
        public override int CurrentPageCount => Roles.Count;
    }

}
