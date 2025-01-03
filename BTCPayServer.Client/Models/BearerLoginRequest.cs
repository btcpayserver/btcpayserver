namespace BTCPayServer.Client.Models;

public class BearerLoginRequest
{
    public string Email { get; set; }
    public string Password { get; set; }
    public string TwoFactorCode { get; set; }
    public string TwoFactorRecoveryCode { get; set; }
}
