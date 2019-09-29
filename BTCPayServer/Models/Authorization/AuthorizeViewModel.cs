using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Models.Authorization
{
    public class AuthorizeViewModel
    {
        [Display(Name = "Application")] public string ApplicationName { get; set; }

        [BindNever] public string RequestId { get; set; }

        [Display(Name = "Scope")] public IEnumerable<string> Scope { get; set; }
    }
}
