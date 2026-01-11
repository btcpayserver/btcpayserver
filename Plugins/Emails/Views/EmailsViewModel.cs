using System.ComponentModel.DataAnnotations;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Validation;

namespace BTCPayServer.Plugins.Emails.Views;

public class EmailsViewModel
{
    public string ModifyPermission { get; set; }
    public string ViewPermission { get; set; }
    public string StoreId { get; set; }
    public EmailSettings Settings { get; set; }
    public bool PasswordSet { get; set; }

    [Display(Name = "Allow Stores use the Server's SMTP email settings as their default")]
    public bool EnableStoresToUseServerEmailSettings { get; set; }

    [MailboxAddress]
    [Display(Name = "Test Email")]
    public string TestEmail { get; set; }

    public EmailsViewModel()
    {
    }

    public EmailsViewModel(EmailSettings settings)
    {
        Settings = settings;
        PasswordSet = !string.IsNullOrWhiteSpace(settings?.Password);
    }

    public bool IsSetup() => Settings?.IsComplete() is true;

    public bool IsFallbackSetup { get; set; }
    public bool IsCustomSMTP { get; set; }
}
