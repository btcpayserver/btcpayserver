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
            
            [Display(Name = "Email")]
            public string Email { get; set; }
            
            [Display(Name = "Name")]
            public string Name { get; set; }
            
            [Display(Name = "Invitation URL")]
            public string InvitationUrl { get; set; }
            
            [Display(Name = "Image")]
            public IFormFile ImageFile { get; set; }
            
            public string ImageUrl { get; set; }
            
            [Display(Name = "Email Confirmed")]
            public bool? EmailConfirmed { get; set; }
            
            [Display(Name = "Approved")]
            public bool? Approved { get; set; }
            
            [Display(Name = "Disabled")]
            public bool Disabled { get; set; }
            
            [Display(Name = "Is Admin")]
            public bool IsAdmin { get; set; }
            
            [Display(Name = "Created")]
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
