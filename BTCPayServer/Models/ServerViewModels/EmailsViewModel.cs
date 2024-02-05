using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Mails;

namespace BTCPayServer.Models.ServerViewModels;

public class ServerEmailsViewModel : EmailsViewModel
{
    [Display(Name = "Allow stores to use the server's SMTP email settings as a default")]
    public bool EnableStoresToUseServerEmailSettings { get; set; }

    public ServerEmailsViewModel()
    {
    }

    public ServerEmailsViewModel(EmailSettings settings) : base(settings)
    {
    }
}
