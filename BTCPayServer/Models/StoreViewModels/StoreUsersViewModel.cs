using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoreUsersViewModel
    {
        public class StoreUserViewModel
        {
            [Display(Name = "Email")]
            public string Email { get; set; }

            [Display(Name = "Role")]
            public string Role { get; set; }

            public string ImageUrl { get; set; }
            public string Id { get; set; }

            /// <summary>Null for members, set for rows that are still an invitation.</summary>
            public DateTimeOffset? InvitedAt { get; set; }
            public DateTimeOffset? Expiry { get; set; }
            public bool IsPending => InvitedAt.HasValue;
            public bool IsExpired { get; set; }

            /// <summary>The signed-in user, so the row can be badged and protected.</summary>
            public bool IsCurrentUser { get; set; }

            /// <summary>Last owner: their role cannot be changed and they cannot be removed.</summary>
            public bool IsLocked { get; set; }
        }

        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; }
        public string StoreId { get; set; }

        [Display(Name = "Role")]
        public string Role { get; set; }

        [Display(Name = "Require the user to accept the invitation")]
        public bool RequireInvitation { get; set; } = true;

        [BindNever]
        public bool CanSkipInvitation { get; set; }


        /// <summary>Members and pending invitations in one list, members first.</summary>
        public List<StoreUserViewModel> Users { get; set; } = new();

        public string Command { get; set; }
    }
}
