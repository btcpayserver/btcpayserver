using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Mails;

namespace BTCPayServer.Models.ServerViewModels
{
    public class EmailsViewModel
    {
        public EmailSettings Settings
        {
            get; set;
        }

        [EmailAddress]
        [Display(Name = "Test Email")]
        public string TestEmail
        {
            get; set;
        }
    }
}
