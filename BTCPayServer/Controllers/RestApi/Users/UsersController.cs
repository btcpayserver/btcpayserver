using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Hosting.OpenApi;
using BTCPayServer.Security;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NSwag.Annotations;

namespace BTCPayServer.Controllers.RestApi.Users
{
    [ApiController]
    [IncludeInOpenApiDocs]
    [OpenApiTags("Users")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BTCPayServerOptions _btcPayServerOptions;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly SettingsRepository _settingsRepository;
        private readonly EventAggregator _eventAggregator;

        public UsersController(UserManager<ApplicationUser> userManager, BTCPayServerOptions btcPayServerOptions,
            RoleManager<IdentityRole> roleManager, SettingsRepository settingsRepository,
            EventAggregator eventAggregator)
        {
            _userManager = userManager;
            _btcPayServerOptions = btcPayServerOptions;
            _roleManager = roleManager;
            _settingsRepository = settingsRepository;
            _eventAggregator = eventAggregator;
        }

        [OpenApiOperation("Get current user information", "View information about the current user")]
        [SwaggerResponse(StatusCodes.Status200OK, typeof(ApplicationUserData),
            Description = "Information about the current user")]
        [Authorize(Policy = Policies.CanModifyProfile.Key, AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
        [HttpGet("~/api/v1/users/me")]
        public async Task<ActionResult<ApplicationUserData>> GetCurrentUser()
        {
            var user = await _userManager.GetUserAsync(User);
            return FromModel(user);
        }

        [OpenApiOperation("Create user", "Create a new user")]
        [SwaggerResponse(StatusCodes.Status201Created, typeof(ApplicationUserData),
            Description = "Information about the new user")]
        [SwaggerResponse(StatusCodes.Status422UnprocessableEntity, typeof(ValidationProblemDetails),
            Description = "A list of validation errors that occurred")]
        [SwaggerResponse(StatusCodes.Status400BadRequest, typeof(ValidationProblemDetails),
            Description = "A list of errors that occurred when creating the user")]
        [Authorize(Policy = Policies.CanCreateUser.Key, AuthenticationSchemes = AuthenticationSchemes.ApiKey)]
        [HttpPost("~/api/v1/users")]
        public async Task<ActionResult<ApplicationUserData>> CreateUser(CreateApplicationUserRequest request)
        {
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
            var identityResult = await _userManager.CreateAsync(user);
            if (!identityResult.Succeeded)
            {
                AddErrors(identityResult);
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

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
    }

    [ModelMetadataType(typeof(CreateApplicationUserRequestMetadata))]
    public class CreateApplicationUserRequest : BTCPayServer.Client.Models.CreateApplicationUserRequest
    {
        
    }

    public class CreateApplicationUserRequestMetadata
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }
    }
}
