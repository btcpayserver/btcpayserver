using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.ServerViewModels
{
    public class EmailsViewModel
    {
        public string StatusMessage
        {
            get; set;
        }
        public EmailSettings Settings
        {
            get; set;
        }
        
        [EmailAddress]
        public string TestEmail
        {
            get; set;
        }
    }
}
