namespace BTCPayApp.CommonServer.Models;

public class AcceptInviteResult(string email)
{
    public string Email { get; init; } = email;
    public bool? RequiresUserApproval { get; set; }
    public bool? EmailHasBeenConfirmed { get; set; }
    public string? PasswordSetCode { get; set; }
}
