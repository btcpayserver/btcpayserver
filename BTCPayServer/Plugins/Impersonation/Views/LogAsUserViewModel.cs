#nullable enable

namespace BTCPayServer.Plugins.Impersonation.Views;

public class LogAsUserViewModel
{
    public string? ReturnUrl { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}
