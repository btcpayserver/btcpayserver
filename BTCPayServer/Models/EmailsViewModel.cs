using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Mails;
using BTCPayServer.Validation;

namespace BTCPayServer.Models;

public class EmailsViewModel
{
    public EmailSettings Settings { get; set; }
    public bool PasswordSet { get; set; }

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
