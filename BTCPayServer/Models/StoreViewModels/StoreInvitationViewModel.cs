using System;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Models.StoreViewModels;

public class StoreInvitationViewModel
{
    public string Token { get; set; }
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public string Role { get; set; }
    public DateTimeOffset Expiry { get; set; }
    public bool IsExpired { get; set; }
    public string Error { get; set; }

    [BindNever]
    public bool IsForAnotherUser { get; set; }
    public string InvitedEmail { get; set; }
}
