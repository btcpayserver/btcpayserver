using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Fido2;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldBearerTokenController : ControllerBase
    {
        private const string Scheme = AuthenticationSchemes.GreenfieldBearer;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserLoginCodeService _userLoginCodeService;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly IOptionsMonitor<BearerTokenOptions> _bearerTokenOption;

        public GreenfieldBearerTokenController(
            UserManager<ApplicationUser> userManager,
            UserLoginCodeService userLoginCodeService,
            SignInManager<ApplicationUser> signInManager,
            IOptionsMonitor<BearerTokenOptions> bearerTokenOption,
            TimeProvider timeProvider,
            Logs logs)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _userLoginCodeService = userLoginCodeService;
            _bearerTokenOption = bearerTokenOption;
            _timeProvider = timeProvider;
            _logger = logs.PayServer;
        }

        [AllowAnonymous]
        [HttpPost("~/api/v1/bearer/login")]
        [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<Results<Ok<AccessTokenResponse>, EmptyHttpResult, ProblemHttpResult>> Login(BearerLoginRequest login)
        {
            var errorMessage = "Invalid login attempt.";
            if (ModelState.IsValid)
            {
                // Require the user to pass basic checks (approval, confirmed email, not disabled) before they can log on
                var user = await _userManager.FindByEmailAsync(login.Email);
                if (!UserService.TryCanLogin(user, out var message))
                {
                    return TypedResults.Problem(message, statusCode: 401);
                }

                _signInManager.AuthenticationScheme = Scheme;
                var signInResult = await _signInManager.PasswordSignInAsync(login.Email, login.Password, true, true);
                if (signInResult.RequiresTwoFactor)
                {
                    if (!string.IsNullOrEmpty(login.TwoFactorCode))
                        signInResult = await _signInManager.TwoFactorAuthenticatorSignInAsync(login.TwoFactorCode, true, true);
                    else if (!string.IsNullOrEmpty(login.TwoFactorRecoveryCode))
                        signInResult = await _signInManager.TwoFactorRecoveryCodeSignInAsync(login.TwoFactorRecoveryCode);
                }
                
                // TODO: Add FIDO and LNURL Auth
                
                if (signInResult.IsLockedOut)
                {
                    _logger.LogWarning("User {Email} tried to log in, but is locked out", user!.Email);
                }
                else if (signInResult.Succeeded)
                {
                    _logger.LogInformation("User {Email} logged in", user!.Email);
                    return TypedResults.Empty;
                }

                errorMessage = signInResult.ToString();
            }

            return TypedResults.Problem(errorMessage, statusCode: 401);
        }
        
        [AllowAnonymous]
        [HttpPost("~/api/v1/bearer/login/code")]
        [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<Results<Ok<AccessTokenResponse>, EmptyHttpResult, ProblemHttpResult>> LoginWithCode([FromBody] string loginCode)
        {
            const string errorMessage = "Invalid login attempt.";
            if (!string.IsNullOrEmpty(loginCode))
            {
                var code = loginCode.Split(';').First();
                var userId = _userLoginCodeService.Verify(code);
                var user = userId is null ? null : await _userManager.FindByIdAsync(userId);
                if (!UserService.TryCanLogin(user, out var message))
                {
                    return TypedResults.Problem(message, statusCode: 401);
                }

                _signInManager.AuthenticationScheme = Scheme;
                await _signInManager.SignInAsync(user!, false, "LoginCode");

                _logger.LogInformation("User {Email} logged in with a login code", user.Email);
                return TypedResults.Empty;
            }

            return TypedResults.Problem(errorMessage, statusCode: 401);
        }

        [AllowAnonymous]
        [HttpPost("~/api/v1/bearer/refresh")]
        [RateLimitsFilter(ZoneLimits.Login, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<Results<Ok<AccessTokenResponse>, UnauthorizedHttpResult, SignInHttpResult, ChallengeHttpResult>> Refresh(BearerRefreshRequest refresh)
        {
            var authenticationTicket = _bearerTokenOption.Get(Scheme).RefreshTokenProtector.Unprotect(refresh.RefreshToken);
            var expiresUtc = authenticationTicket?.Properties.ExpiresUtc;

            ApplicationUser user = null;
            int num;
            if (expiresUtc.HasValue)
            {
                DateTimeOffset valueOrDefault = expiresUtc.GetValueOrDefault();
                num = _timeProvider.GetUtcNow() >= valueOrDefault ? 1 : 0;
            }
            else
                num = 1;
            bool flag = num != 0;
            if (!flag)
            {
                _signInManager.AuthenticationScheme = Scheme;
                user = await _signInManager.ValidateSecurityStampAsync(authenticationTicket?.Principal);
            }
            
            return user != null
                ? TypedResults.SignIn(await _signInManager.CreateUserPrincipalAsync(user), authenticationScheme: Scheme)
                : TypedResults.Challenge(authenticationSchemes: [Scheme]);
        }
        
        [HttpPost("~/api/v1/bearer/logout")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.GreenfieldBearer)]
        public async Task<IResult> Logout()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                await _signInManager.SignOutAsync();
                _logger.LogInformation("User {Email} logged out", user.Email);
                return Results.Ok();
            }
            return Results.Unauthorized();
        }
    }
}
