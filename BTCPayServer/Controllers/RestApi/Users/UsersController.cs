using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Security;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.Controllers.RestApi.Users
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BTCPayServerOptions _btcPayServerOptions;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly SettingsRepository _settingsRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly IPasswordValidator<ApplicationUser> _passwordValidator;

        public UsersController(UserManager<ApplicationUser> userManager, BTCPayServerOptions btcPayServerOptions,
            RoleManager<IdentityRole> roleManager, SettingsRepository settingsRepository,
            EventAggregator eventAggregator,
            IPasswordValidator<ApplicationUser> passwordValidator)
        {
            _userManager = userManager;
            _btcPayServerOptions = btcPayServerOptions;
            _roleManager = roleManager;
            _settingsRepository = settingsRepository;
            _eventAggregator = eventAggregator;
            _passwordValidator = passwordValidator;
        }

        [Authorize(Policy = Policies.CanModifyProfile.Key, AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
        [HttpGet("~/api/v1/users/me")]
        public async Task<ActionResult<ApplicationUserData>> GetCurrentUser()
        {
            var user = await _userManager.GetUserAsync(User);
            return FromModel(user);
        }

        [Authorize(Policy = Policies.CanCreateUser.Key, AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
        [HttpPost("~/api/v1/users")]
        public async Task<ActionResult<ApplicationUserData>> CreateUser(CreateApplicationUserRequest request)
        {
            if (request?.Email is null)
                return BadRequest(CreateValidationProblem(nameof(request.Email), "Email is missing"));
            if (!Validation.EmailValidator.IsEmail(request.Email))
            {
                return BadRequest(CreateValidationProblem(nameof(request.Email), "Invalid email"));
            }
            if (request?.Password is null)
                return BadRequest(CreateValidationProblem(nameof(request.Password), "Password is missing"));
            var policies = await _settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            var anyAdmin = (await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin)).Any();
            var admin = request.IsAdministrator.GetValueOrDefault(!anyAdmin);
            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                RequiresEmailConfirmation = policies.RequiresConfirmedEmail,
                EmailConfirmed = request.EmailConfirmed.GetValueOrDefault(false)
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
            var identityResult = await _userManager.CreateAsync(user, request.Password);
            if (!identityResult.Succeeded)
            {
                foreach (var error in identityResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return BadRequest(new ValidationProblemDetails(ModelState));
            }
            else if (admin)
            {
                await _roleManager.CreateAsync(new IdentityRole(Roles.ServerAdmin));
                await _userManager.AddToRoleAsync(user, Roles.ServerAdmin);
            }
            _eventAggregator.Publish(new UserRegisteredEvent() {Request = Request, User = user, Admin = admin});
            return CreatedAtAction("", user);
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
