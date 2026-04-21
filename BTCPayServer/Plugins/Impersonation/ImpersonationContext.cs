#nullable enable
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.Impersonation;

public class ImpersonationContext(
    IHttpContextAccessor httpContextAccessor,
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions
    )
{
    readonly HttpContext _context = httpContextAccessor.HttpContext!;
    readonly CookieAuthenticationOptions _cookieOptions = cookieOptions.Get(IdentityConstants.ApplicationScheme);
    string ImpersonatorCookieName => _cookieOptions.Cookie.Name + "_prev";

    public ClaimsPrincipal? Revert()
    {
        if (_cookieOptions.CookieManager.GetRequestCookie(_context, ImpersonatorCookieName) is string v)
        {
            _cookieOptions.CookieManager.DeleteCookie(_context, ImpersonatorCookieName, _cookieOptions.Cookie.Build(_context));
            var ticket = _cookieOptions.TicketDataFormat.Unprotect(v);
            if (ticket is null)
                return null;
            _cookieOptions.CookieManager.AppendResponseCookie(_context, _cookieOptions.Cookie.Name ?? "", v, _cookieOptions.Cookie.Build(_context));
            return ticket.Principal;
        }
        return null;
    }

    public void StartImpersonation()
    {
        if (_cookieOptions.CookieManager.GetRequestCookie(_context, _cookieOptions.Cookie.Name ?? "") is string v)
        {
            _cookieOptions.CookieManager.AppendResponseCookie(_context, ImpersonatorCookieName, v, _cookieOptions.Cookie.Build(_context));
        }
    }

    public bool IsImpersonating => _cookieOptions.CookieManager.GetRequestCookie(_context, ImpersonatorCookieName) is not null;
}
