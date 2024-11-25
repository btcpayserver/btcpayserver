#nullable enable
namespace BTCPayServer.Client.App.Models;

public class AcceptInviteRequest
{
    public string? UserId { get; set; }
    public string? Code { get; set; }
}
