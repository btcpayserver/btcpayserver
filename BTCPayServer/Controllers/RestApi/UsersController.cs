using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Security;
using BTCPayServer.Security.APIKeys;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NicolasDorier.RateLimits;
using BTCPayServer.Client;

namespace BTCPayServer.Controllers.RestApi
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BTCPayServerOptions _btcPayServerOptions;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly SettingsRepository _settingsRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly IPasswordValidator<ApplicationUser> _passwordValidator;
        private readonly RateLimitService _throttleService;
        private readonly BTCPayServerOptions _options;
        private readonly IAuthorizationService _authorizationService;

        public UsersController(UserManager<ApplicationUser> userManager, BTCPayServerOptions btcPayServerOptions,
            RoleManager<IdentityRole> roleManager, SettingsRepository settingsRepository,
            EventAggregator eventAggregator,
            IPasswordValidator<ApplicationUser> passwordValidator,
            RateLimitService throttleService,
            BTCPayServerOptions options,
            IAuthorizationService authorizationService)
        {
            _userManager = userManager;
            _btcPayServerOptions = btcPayServerOptions;
            _roleManager = roleManager;
            _settingsRepository = settingsRepository;
            _eventAggregator = eventAggregator;
            _passwordValidator = passwordValidator;
            _throttleService = throttleService;
            _options = options;
            _authorizationService = authorizationService;
        }

        [Authorize(Policy = Policies.CanViewProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/users/me")]
        public async Task<ActionResult<ApplicationUserData>> GetCurrentUser()
        {
            var user = await _userManager.GetUserAsync(User);
            return FromModel(user);
        }

        [AllowAnonymous]
        [HttpPost("~/api/v1/users")]
        public async Task<ActionResult<ApplicationUserData>> CreateUser(CreateApplicationUserRequest request, CancellationToken cancellationToken = default)
        {
            if (request?.Email is null)
                return BadRequest(CreateValidationProblem(nameof(request.Email), "Email is missing"));
            if (!Validation.EmailValidator.IsEmail(request.Email))
            {
                return BadRequest(CreateValidationProblem(nameof(request.Email), "Invalid email"));
            }
            if (request?.Password is null)
                return BadRequest(CreateValidationProblem(nameof(request.Password), "Password is missing"));
            var anyAdmin = (await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin)).Any();
            var policies = await _settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            var isAuth = User.Identity.AuthenticationType == APIKeyConstants.AuthenticationType;

            // If registration are locked and that an admin exists, don't accept unauthenticated connection
            if (anyAdmin && policies.LockSubscription && !isAuth)
                return Unauthorized();

            // Even if subscription are unlocked, it is forbidden to create admin unauthenticated
            if (anyAdmin && request.IsAdministrator is true && !isAuth)
                return Forbid(AuthenticationSchemes.Greenfield);
            // You are de-facto admin if there is no other admin, else you need to be auth and pass policy requirements
            bool isAdmin = anyAdmin ? (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanModifyServerSettings))).Succeeded
                                     && (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.Unrestricted))).Succeeded
                                     && isAuth
                                    : true;
            // You need to be admin to create an admin
            if (request.IsAdministrator is true && !isAdmin)
                return Forbid(AuthenticationSchemes.Greenfield);

            if (!isAdmin && policies.LockSubscription)
            {
                // If we are not admin and subscriptions are locked, we need to check the Policies.CanCreateUser.Key permission
                var canCreateUser = (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanCreateUser))).Succeeded;
                if (!isAuth || !canCreateUser)
                    return Forbid(AuthenticationSchemes.Greenfield);
            }

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                RequiresEmailConfirmation = policies.RequiresConfirmedEmail
            };
            var passwordValidation = await this._passwordValidator.ValidateAsync(_userManager, user, request.Password);
            if (!passwordValidation.Succeeded)
            {
                foreach (var error in passwordValidation.Errors)
                {
                    ModelState.AddModelError(nameof(request.Password), error.Description);
                }
                return BadRequest(new ValidationProblemDetails(ModelState));
            }
            if (!isAdmin)
            {
                if (!await _throttleService.Throttle(ZoneLimits.Register, this.HttpContext.Connection.RemoteIpAddress, cancellationToken))
                    return new TooManyRequestsResult(ZoneLimits.Register);
            }
            var identityResult = await _userManager.CreateAsync(user, request.Password);
            if (!identityResult.Succeeded)
            {
                foreach (var error in identityResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return BadRequest(new ValidationProblemDetails(ModelState));
            }

            if (request.IsAdministrator is true)
            {
                if (!anyAdmin)
                {
                    await _roleManager.CreateAsync(new IdentityRole(Roles.ServerAdmin));
                }
                await _userManager.AddToRoleAsync(user, Roles.ServerAdmin);
                if (!anyAdmin)
                {
                    if (_options.DisableRegistration)
                    {
                        // automatically lock subscriptions now that we have our first admin
                        Logs.PayServer.LogInformation("First admin created, disabling subscription (disable-registration is set to true)");
                        policies.LockSubscription = true;
                        await _settingsRepository.UpdateSetting(policies);
                    }
                }
            }
            _eventAggregator.Publish(new UserRegisteredEvent() {Request = Request, User = user, Admin = request.IsAdministrator is true });
            return CreatedAtAction(string.Empty, user);
        }

        private ValidationProblemDetails CreateValidationProblem(string propertyName, string errorMessage)
        {
            var modelState = new ModelStateDictionary();
            modelState.AddModelError(propertyName, errorMessage);
            return new ValidationProblemDetails(modelState);
        }

        private static ApplicationUserData FromModel(ApplicationUser data)
        {
            return new ApplicationUserData()
            {
                Id = data.Id,
                Email = data.Email,
                EmailConfirmed = data.EmailConfirmed,
                RequiresEmailConfirmation = data.RequiresEmailConfirmation
            };
        }
    }
}
