#nullable enable
using BTCPayServer.Client.Models;

namespace BTCPayServer
{
    public static class ValidationExtensions
    {
        public static void Validate(this EmailSettingsData request, Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelState)
        {
            if (!string.IsNullOrEmpty(request.From) && !MailboxAddressValidator.IsMailboxAddress(request.From))
                modelState.AddModelError(nameof(request.From), "Invalid email address");
        }
    }
}
