using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Mails;

namespace BTCPayServer.Models.ServerViewModels;

public class ServerEmailsViewModel : EmailsViewModel
{
    [Display(Name = "Allow Stores use the Server's SMTP email settings as their default")]
    public bool EnableStoresToUseServerEmailSettings { get; set; }

    public ServerEmailsViewModel()
    {
    }

    public ServerEmailsViewModel(EmailSettings settings) : base(settings)
    {
    }
}
