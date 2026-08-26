using System;

namespace BTCPayServer.Models.StoreViewModels;

public class StoreInvitationSentViewModel
{
    public string StoreId { get; set; }
    public string Email { get; set; }
    public string Role { get; set; }
    public string InvitationUrl { get; set; }
    public bool EmailSent { get; set; }
    public DateTimeOffset Expiry { get; set; }

    /// <summary>False when simply looking the link up again, which sends nothing.</summary>
    public bool JustSent { get; set; }
}
