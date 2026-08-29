#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services;

public class BTCPayServerSecurityStampValidator : SecurityStampValidator<ApplicationUser>
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly SecurityStampInvalidator _securityStampInvalidator;

    public BTCPayServerSecurityStampValidator(
        IOptions<SecurityStampValidatorOptions> options,
        SignInManager<ApplicationUser> signInManager,
        ILoggerFactory logger,
        SecurityStampInvalidator securityStampInvalidator) : base(options, signInManager, logger)
    {
        _signInManager = signInManager;
        _securityStampInvalidator = securityStampInvalidator;
    }

    /// <summary>
    /// Security stamps are normally revalidated every <see cref="SecurityStampValidatorOptions.ValidationInterval"/>.
    /// This forces earlier validation for selected users, so cookie claims such as roles are refreshed immediately.
    /// </summary>
    public class SecurityStampInvalidator
    {
        ConcurrentDictionary<string, DateTimeOffset> _InvalidatedStamps = new ConcurrentDictionary<string, DateTimeOffset>();
        public bool HasAny => !_InvalidatedStamps.IsEmpty;

        /// <summary>
        /// Forces the logged-in user's cookie claims, such as roles, to be refreshed immediately.
        /// </summary>
        /// <param name="user"></param>
        public void Invalidate(string user) => _InvalidatedStamps.AddOrUpdate(user, _ => DateTimeOffset.UtcNow, (_, _) => DateTimeOffset.UtcNow);

        public void Cleanup(TimeSpan validationInterval)
        {
            if (_InvalidatedStamps.IsEmpty)
                return;
            var now = DateTimeOffset.UtcNow;
            foreach (var kv in _InvalidatedStamps)
            {
                if (now - kv.Value > validationInterval)
                    _InvalidatedStamps.TryRemove(kv.Key, out _);
            }
        }

        public bool TryGetValue(string id, out DateTimeOffset invalidatedAt)
        => _InvalidatedStamps.TryGetValue(id, out invalidatedAt);
    }

    public override async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (_securityStampInvalidator.HasAny &&
            context.Principal.GetIdOrNull() is string id &&
            _securityStampInvalidator.TryGetValue(id, out var invalidatedAt) &&
            context.Properties.IssuedUtc is {} issuedAt &&
            issuedAt < invalidatedAt)
        {
            context.Properties.IssuedUtc = null;
        }
        _securityStampInvalidator.Cleanup(Options.ValidationInterval);
        await base.ValidateAsync(context);
    }
}
