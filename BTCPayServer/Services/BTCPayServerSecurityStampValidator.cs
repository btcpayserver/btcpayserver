#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services;

public class BTCPayServerSecurityStampValidator(
    IOptions<SecurityStampValidatorOptions> options,
    SignInManager<ApplicationUser> signInManager,
    ILoggerFactory logger,
    UserManager<ApplicationUser> userManager,
    BTCPayServerSecurityStampValidator.DisabledUsers disabledUsers)
    : SecurityStampValidator<ApplicationUser>(options, signInManager, logger)
{
    public class DisabledUsers
    {
        ConcurrentDictionary<string, DateTimeOffset> _DisabledUsers = new ConcurrentDictionary<string, DateTimeOffset>();
        public bool HasAny => !_DisabledUsers.IsEmpty;

        /// <summary>
        /// Note that you also need to invalidate the security stamp of the user
        /// </summary>
        /// <param name="user"></param>
        public void Add(string user)
        {
            _DisabledUsers.TryAdd(user, DateTimeOffset.UtcNow);
        }

        public void Remove(string user)
        {
            _DisabledUsers.TryRemove(user, out _);
        }

        public bool Contains(string id) => _DisabledUsers.ContainsKey(id);

        public void Cleanup(TimeSpan validationInterval)
        {
            if (_DisabledUsers.IsEmpty)
                return;
            var now = DateTimeOffset.UtcNow;
            foreach (var kv in _DisabledUsers)
            {
                if (now - kv.Value > validationInterval)
                    Remove(kv.Key);
            }
        }
    }

    public override async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (disabledUsers.HasAny &&
            context.Principal is not null &&
            userManager.GetUserId(context.Principal) is string id &&
            disabledUsers.Contains(id))
        {
            context.Properties.IssuedUtc = null;
        }
        disabledUsers.Cleanup(Options.ValidationInterval);
        await base.ValidateAsync(context);
    }
}
