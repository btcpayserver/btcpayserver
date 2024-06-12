using System.ComponentModel.DataAnnotations;

namespace BTCPayApp.CommonServer.Models;

public class AcceptInviteRequest
{
    [Required]
    public string? UserId { get; init; }
    
    [Required]
    public string? Code { get; init; }
}
