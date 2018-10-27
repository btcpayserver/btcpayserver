using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.ServerViewModels
{
    public class UserViewModel
    {
        public string Id { get; set; }
        public string Email { get; set; }
        [Display(Name = "Is admin")]
        public bool IsAdmin { get; set; }
        public string StatusMessage { get; set; }
    }
}
