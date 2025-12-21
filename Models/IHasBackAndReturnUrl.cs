#nullable enable
using System;
using BTCPayServer.Controllers;

namespace BTCPayServer.Models
{
    public interface IHasBackAndReturnUrl
    {
        string? BackUrl { get; set; }
        string? ReturnUrl { get; set; }
        (string? backUrl, string? returnUrl) NormalizeBackAndReturnUrl()
        {
            var backUrl = BackUrl;
            if (backUrl is not null && ReturnUrl is not null)
            {
                var queryParam = $"returnUrl={Uri.EscapeDataString(ReturnUrl)}";
                if (backUrl.Contains('?'))
                    backUrl = $"{backUrl}&{queryParam}";
                else
                    backUrl = $"{backUrl}?{queryParam}";
            }
            return (backUrl, ReturnUrl);
        }
    }
}
