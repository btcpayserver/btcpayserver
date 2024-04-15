namespace BTCPayApp.CommonServer;

public class SignupResult
{
    public string? Email { get; set; }
    public bool RequiresConfirmedEmail { get; set; }
    public bool RequiresUserApproval { get; set; }
}
