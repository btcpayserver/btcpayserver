using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Mails;
using BTCPayServer.Validation;

namespace BTCPayServer.Models;

public class EmailsViewModel
{
    public EmailSettings Settings { get; set; }
    public EmailSettings FallbackSettings { get; set; }
    public bool PasswordSet { get; set; }
        
    [MailboxAddress]
    [Display(Name = "Test Email")]
    public string TestEmail { get; set; }

    public EmailsViewModel()
    {
    }

    public EmailsViewModel(EmailSettings settings, EmailSettings fallbackSettings = null)
    {
        Settings = settings;
        FallbackSettings = fallbackSettings;
        PasswordSet = !string.IsNullOrEmpty(settings?.Password);
    }
        
    public bool IsSetup() => Settings?.IsComplete() is true;
    public bool IsFallbackSetup() => FallbackSettings?.IsComplete() is true;
    public bool UsesFallback() => IsFallbackSetup() && Settings == FallbackSettings;
}
