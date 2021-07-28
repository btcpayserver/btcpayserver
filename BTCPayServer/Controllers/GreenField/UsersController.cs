#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Security;
using BTCPayServer.Security.GreenField;
using BTCPayServer.Services;
using BTCPayServer.Storage.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly SettingsRepository _settingsRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly IPasswordValidator<ApplicationUser> _passwordValidator;
        private readonly RateLimitService _throttleService;
        private readonly BTCPayServerOptions _options;
        private readonly IAuthorizationService _authorizationService;
        private readonly UserService _userService;

        public UsersController(UserManager<ApplicationUser> userManager, 
            RoleManager<IdentityRole> roleManager, 
            SettingsRepository settingsRepository,
            EventAggregator eventAggregator,
            IPasswordValidator<ApplicationUser> passwordValidator,
            RateLimitService throttleService,
            BTCPayServerOptions options,
            IAuthorizationService authorizationService,
            UserService userService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _settingsRepository = settingsRepository;
            _eventAggregator = eventAggregator;
            _passwordValidator = passwordValidator;
            _throttleService = throttleService;
            _options = options;
            _authorizationService = authorizationService;
            _userService = userService;
        }

        [Authorize(Policy = Policies.CanViewProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/users/me")]
        public async Task<ActionResult<ApplicationUserData>> GetCurrentUser()
        {
            var user = await _userManager.GetUserAsync(User);
            return await FromModel(user);
        }

        [Authorize(Policy = Policies.CanDeleteUser, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/users/me")]
        public async Task<IActionResult> DeleteCurrentUser()
        {
            return await DeleteUser(_userManager.GetUserId(User));
        }

        [AllowAnonymous]
        [HttpPost("~/api/v1/users")]
        public async Task<IActionResult> CreateUser(CreateApplicationUserRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Email is null)
                ModelState.AddModelError(nameof(request.Email), "Email is missing");
            if (!string.IsNullOrEmpty(request.Email) && !Validation.EmailValidator.IsEmail(request.Email))
            {
                ModelState.AddModelError(nameof(request.Email), "Invalid email");
            }
            if (request.Password is null)
                ModelState.AddModelError(nameof(request.Password), "Password is missing");

            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }
            var anyAdmin = (await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin)).Any();
            var policies = await _settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            var isAuth = User.Identity.AuthenticationType == GreenFieldConstants.AuthenticationType;

            // If registration are locked and that an admin exists, don't accept unauthenticated connection
            if (anyAdmin && policies.LockSubscription && !isAuth)
                return Unauthorized();

            // Even if subscription are unlocked, it is forbidden to create admin unauthenticated
            if (anyAdmin && request.IsAdministrator is true && !isAuth)
                return Forbid(AuthenticationSchemes.GreenfieldBasic);
            // You are de-facto admin if there is no other admin, else you need to be auth and pass policy requirements
            bool isAdmin = anyAdmin ? (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanModifyServerSettings))).Succeeded
                                     && (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.Unrestricted))).Succeeded
                                     && isAuth
                                    : true;
            // You need to be admin to create an admin
            if (request.IsAdministrator is true && !isAdmin)
                return Forbid(AuthenticationSchemes.GreenfieldBasic);

            if (!isAdmin && (policies.LockSubscription || (await _settingsRepository.GetPolicies()).DisableNonAdminCreateUserApi))
            {
                // If we are not admin and subscriptions are locked, we need to check the Policies.CanCreateUser.Key permission
                var canCreateUser = (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanCreateUser))).Succeeded;
                if (!isAuth || !canCreateUser)
                    return Forbid(AuthenticationSchemes.GreenfieldBasic);
            }

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                RequiresEmailConfirmation = policies.RequiresConfirmedEmail,
                Created = DateTimeOffset.UtcNow,
            };
            var passwordValidation = await this._passwordValidator.ValidateAsync(_userManager, user, request.Password);
            if (!passwordValidation.Succeeded)
            {
                foreach (var error in passwordValidation.Errors)
                {
                    ModelState.AddModelError(nameof(request.Password), error.Description);
                }
                return this.CreateValidationError(ModelState);
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
                    if (error.Code == "DuplicateUserName")
                        ModelState.AddModelError(nameof(request.Email), error.Description);
                    else
                        ModelState.AddModelError(string.Empty, error.Description);
                }
                return this.CreateValidationError(ModelState);
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
                    var settings = await _settingsRepository.GetSettingAsync<ThemeSettings>();
                    if (settings != null) {
                        settings.FirstRun = false;
                        await _settingsRepository.UpdateSetting(settings);
                    }

                    await _settingsRepository.FirstAdminRegistered(policies, _options.UpdateUrl != null, _options.DisableRegistration);
                }
            }
            _eventAggregator.Publish(new UserRegisteredEvent() { RequestUri = Request.GetAbsoluteRootUri(), User = user, Admin = request.IsAdministrator is true });
            var model = await FromModel(user);
            return CreatedAtAction(string.Empty, model);
        }

        [HttpDelete("~/api/v1/users/{userId}")]
        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return UserNotFound();
            }

            // We can safely delete the user if it's not an admin user
            if (!(await _userService.IsAdminUser(user)))
            {
                await _userService.DeleteUserAndAssociatedData(user);

                return Ok();
            }

            // User shouldn't be deleted if it's the only admin
            if (await IsUserTheOnlyOneAdmin(user))
            {
                return Forbid(AuthenticationSchemes.GreenfieldBasic);
            }

            // Ok, this user is an admin but there are other admins as well so safe to delete
            await _userService.DeleteUserAndAssociatedData(user);

            return Ok();
        }

        private async Task<ApplicationUserData> FromModel(ApplicationUser data)
        {
            var roles = (await _userManager.GetRolesAsync(data)).ToArray();
            return new ApplicationUserData()
            {
                Id = data.Id,
                Email = data.Email,
                EmailConfirmed = data.EmailConfirmed,
                RequiresEmailConfirmation = data.RequiresEmailConfirmation,
                Roles = roles,
                Created = data.Created
            };
        }

        private async Task<bool> IsUserTheOnlyOneAdmin()
        {
            return await IsUserTheOnlyOneAdmin(await _userManager.GetUserAsync(User));
        }

        private async Task<bool> IsUserTheOnlyOneAdmin(ApplicationUser user)
        {
            var isUserAdmin = await _userService.IsAdminUser(user);
            if (!isUserAdmin) {
                return false;
            }

            return (await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin)).Count == 1;
        }

        private IActionResult UserNotFound()
        {
            return this.CreateAPIError(404, "user-not-found", "The user was not found");
        }
    }
}
