using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Mvc.Rendering;

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
