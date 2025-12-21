#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Security;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldUsersController : ControllerBase
    {
        public PoliciesSettings PoliciesSettings { get; }
        public Logs Logs { get; }

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly SettingsRepository _settingsRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly CallbackGenerator _callbackGenerator;
        private readonly IPasswordValidator<ApplicationUser> _passwordValidator;
        private readonly IRateLimitService _throttleService;
        private readonly BTCPayServerOptions _options;
        private readonly IAuthorizationService _authorizationService;
        private readonly UserService _userService;
        private readonly UriResolver _uriResolver;
        private readonly IFileService _fileService;

        public GreenfieldUsersController(UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            SettingsRepository settingsRepository,
            PoliciesSettings policiesSettings,
            EventAggregator eventAggregator,
            CallbackGenerator callbackGenerator,
            IPasswordValidator<ApplicationUser> passwordValidator,
            IRateLimitService throttleService,
            BTCPayServerOptions options,
            IAuthorizationService authorizationService,
            UserService userService,
            UriResolver uriResolver,
            IFileService fileService,
            Logs logs)
        {
            this.Logs = logs;
            _userManager = userManager;
            _roleManager = roleManager;
            _settingsRepository = settingsRepository;
            PoliciesSettings = policiesSettings;
            _eventAggregator = eventAggregator;
            _callbackGenerator = callbackGenerator;
            _passwordValidator = passwordValidator;
            _throttleService = throttleService;
            _options = options;
            _authorizationService = authorizationService;
            _userService = userService;
            _uriResolver = uriResolver;
            _fileService = fileService;
        }

        [Authorize(Policy = Policies.CanViewUsers, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/users/{idOrEmail}")]
        public async Task<IActionResult> GetUser(string idOrEmail)
        {
            var user = await _userManager.FindByIdOrEmail(idOrEmail);
            if (user != null)
            {
                return Ok(await ForAPI(user));
            }
            return this.UserNotFound();
        }

        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/users/{idOrEmail}/lock")]
        public async Task<IActionResult> LockUser(string idOrEmail, LockUserRequest request)
        {
            var user = await _userManager.FindByIdOrEmail(idOrEmail);
            if (user is null)
            {
                return this.UserNotFound();
            }

            var success = await _userService.SetDisabled(user.Id, request.Locked);
            return success is not UserService.SetDisabledResult.Error  ? Ok() : this.CreateAPIError("invalid-state",
                $"{(request.Locked ? "Locking" : "Unlocking")} user failed");
        }

        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/users/{idOrEmail}/approve")]
        public async Task<IActionResult> ApproveUser(string idOrEmail, ApproveUserRequest request)
        {
            var user = await _userManager.FindByIdOrEmail(idOrEmail);
            if (user is null)
            {
                return this.UserNotFound();
            }

            if (user.RequiresApproval)
            {
                var loginLink = _callbackGenerator.ForLogin(user);
                return await _userService.SetUserApproval(user.Id, request.Approved, loginLink)
                    ? Ok()
                    : this.CreateAPIError("invalid-state", $"User is already {(request.Approved ? "approved" : "unapproved")}");
            }
            return this.CreateAPIError("invalid-state", $"{(request.Approved ? "Approving" : "Unapproving")} user failed: No approval required");
        }

        [Authorize(Policy = Policies.CanViewUsers, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/users/")]
        public async Task<ActionResult<ApplicationUserData[]>> GetUsers()
        {
            var usersWithRoles = await _userService.GetUsersWithRoles();
            List<ApplicationUserData> users = [];
            foreach (var user in usersWithRoles)
            {
                users.Add(await UserService.ForAPI<ApplicationUserData>(user.User, user.Roles, _callbackGenerator, _uriResolver, Request));
            }
            return Ok(users);
        }

        [Authorize(Policy = Policies.CanViewProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/users/me")]
        public async Task<ActionResult<ApplicationUserData>> GetCurrentUser()
        {
            var user = await _userManager.GetUserAsync(User);
            return await ForAPI(user!);
        }

        [Authorize(Policy = Policies.CanModifyProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/users/me")]
        public async Task<IActionResult> UpdateCurrentUser(UpdateApplicationUserRequest request, CancellationToken cancellationToken = default)
        {
            var user = await _userManager.GetUserAsync(User);
            if (User.Identity is null || user is null)
                return this.CreateAPIError(401, "unauthenticated", "User is not authenticated");

            if (!string.IsNullOrEmpty(request.Email) && !request.Email.IsValidEmail())
            {
                ModelState.AddModelError(nameof(request.Email), "Invalid email");
            }

            bool needUpdate = false;
            var setNewPassword = !string.IsNullOrEmpty(request.NewPassword);
            if (setNewPassword)
            {
                if (!await _userManager.CheckPasswordAsync(user, request.CurrentPassword))
                {
                    ModelState.AddModelError(nameof(request.CurrentPassword), "The current password is not correct.");
                }
                else
                {
                    var passwordValidation = await _passwordValidator.ValidateAsync(_userManager, user, request.NewPassword);
                    if (passwordValidation.Succeeded)
                    {
                        var setUserResult = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
                        if (!setUserResult.Succeeded)
                        {
                            ModelState.AddModelError(nameof(request.Email), "Unexpected error occurred setting password for user.");
                        }
                    }
                    else
                    {
                        foreach (var error in passwordValidation.Errors)
                        {
                            ModelState.AddModelError(nameof(request.NewPassword), error.Description);
                        }
                    }
                }
            }

            var email = user.Email;
            if (!string.IsNullOrEmpty(request.Email) && request.Email != email)
            {
                var setUserResult = await _userManager.SetUserNameAsync(user, request.Email);
                if (!setUserResult.Succeeded)
                {
                    ModelState.AddModelError(nameof(request.Email), "Unexpected error occurred setting email for user.");
                }
                var setEmailResult = await _userManager.SetEmailAsync(user, request.Email);
                if (!setEmailResult.Succeeded)
                {
                    ModelState.AddModelError(nameof(request.Email), "Unexpected error occurred setting email for user.");
                }
            }

            var blob = user.GetBlob() ?? new();
            if (request.Name is not null && request.Name != blob.Name)
            {
                blob.Name = request.Name;
                needUpdate = true;
            }

            if (request.ImageUrl is not null && request.ImageUrl != blob.ImageUrl)
            {
                blob.ImageUrl = request.ImageUrl;
                needUpdate = true;
            }
            user.SetBlob(blob);

            if (ModelState.IsValid && needUpdate)
            {
                var identityResult = await _userManager.UpdateAsync(user);
                if (!identityResult.Succeeded)
                {
                    foreach (var error in identityResult.Errors)
                    {
                        if (error.Code == "DuplicateUserName")
                            ModelState.AddModelError(nameof(request.Email), error.Description);
                        else
                            ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
                else
                {
                    _eventAggregator.Publish(new UserEvent.Updated(user));
                }
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            var model = await ForAPI(user);
            return Ok(model);
        }

        [Authorize(Policy = Policies.CanModifyProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/users/me/picture")]
        public async Task<IActionResult> UploadCurrentUserProfilePicture(IFormFile? file)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return this.UserNotFound();

            UploadImageResultModel? upload = null;
            if (file is null)
                ModelState.AddModelError(nameof(file), "Invalid file");
            else
            {
                upload = await _fileService.UploadImage(file, user.Id);
                if (!upload.Success)
                    ModelState.AddModelError(nameof(file), upload.Response);
            }
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            try
            {
                var storedFile = upload!.StoredFile!;
                var blob = user.GetBlob() ?? new UserBlob();
                var fileIdUri = new UnresolvedUri.FileIdUri(storedFile.Id);
                blob.ImageUrl = fileIdUri.ToString();
                user.SetBlob(blob);
                await _userManager.UpdateAsync(user);
                _eventAggregator.Publish(new UserEvent.Updated(user));
                var model = await ForAPI(user);
                return Ok(model);
            }
            catch (Exception e)
            {
                return this.CreateAPIError(404, "file-upload-failed", e.Message);
            }
        }

        [Authorize(Policy = Policies.CanModifyProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/users/me/picture")]
        public async Task<IActionResult> DeleteCurrentUserProfilePicture()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return this.UserNotFound();

            var blob = user.GetBlob() ?? new UserBlob();
            if (!string.IsNullOrEmpty(blob.ImageUrl))
            {
                var fileId = (UnresolvedUri.Create(blob.ImageUrl) as UnresolvedUri.FileIdUri)?.FileId;
                if (!string.IsNullOrEmpty(fileId)) await _fileService.RemoveFile(fileId, user.Id);
                blob.ImageUrl = null;
                user.SetBlob(blob);
                await _userManager.UpdateAsync(user);
                _eventAggregator.Publish(new UserEvent.Updated(user));
            }
            return Ok();
        }

        [Authorize(Policy = Policies.CanDeleteUser, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/users/me")]
        public async Task<IActionResult> DeleteCurrentUser()
        {
            return await DeleteUser(_userManager.GetUserId(User)!);
        }

        [AllowAnonymous]
        [HttpPost("~/api/v1/users")]
        public async Task<IActionResult> CreateUser(CreateApplicationUserRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Email is null)
                ModelState.AddModelError(nameof(request.Email), "Email is missing");
            if (!MailboxAddressValidator.IsMailboxAddress(request.Email))
                ModelState.AddModelError(nameof(request.Email), "Invalid email");

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            if (User.Identity is null)
                throw new JsonHttpException(this.StatusCode(401));
            var anyAdmin = (await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin)).Any();
            var policies = await _settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            var isAuth = User.Identity.AuthenticationType == GreenfieldConstants.AuthenticationType;

            // If registration are locked and that an admin exists, don't accept unauthenticated connection
            if (anyAdmin && policies.LockSubscription && !isAuth)
                return this.CreateAPIError(401, "unauthenticated", "New user creation isn't authorized to users who are not admin");

            // Even if subscription are unlocked, it is forbidden to create admin unauthenticated
            if (anyAdmin && request.IsAdministrator is true && !isAuth)
                return this.CreateAPIError(401, "unauthenticated", "New admin creation isn't authorized to users who are not admin");
            // You are de-facto admin if there is no other admin, else you need to be auth and pass policy requirements
            bool isAdmin = anyAdmin ? (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanModifyServerSettings))).Succeeded
                                     && (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.Unrestricted))).Succeeded
                                     && isAuth
                                    : true;
            // You need to be admin to create an admin
            if (request.IsAdministrator is true && !isAdmin)
                return this.CreateAPIPermissionError(Policies.Unrestricted, $"Insufficient API Permissions. Please use an API key with permission: {Policies.Unrestricted} and be an admin.");

            if (!isAdmin && (policies.LockSubscription || PoliciesSettings.DisableNonAdminCreateUserApi))
            {
                // If we are not admin and subscriptions are locked, we need to check the Policies.CanCreateUser.Key permission
                var canCreateUser = (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanCreateUser))).Succeeded;
                if (!isAuth || !canCreateUser)
                    return this.CreateAPIPermissionError(Policies.CanCreateUser);
            }

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                RequiresEmailConfirmation = policies.RequiresConfirmedEmail,
                RequiresApproval = policies.RequiresUserApproval,
                Created = DateTimeOffset.UtcNow,
                Approved = isAdmin // auto-approve first admin and users created by an admin
            };

            var blob = user.GetBlob() ?? new();
            blob.Name = request.Name;
            blob.ImageUrl = request.ImageUrl;
            user.SetBlob(blob);
            var hasPassword = !string.IsNullOrEmpty(request.Password);
            if (hasPassword)
            {
                var passwordValidation = await _passwordValidator.ValidateAsync(_userManager, user, request.Password);
                if (!passwordValidation.Succeeded)
                {
                    foreach (var error in passwordValidation.Errors)
                    {
                        ModelState.AddModelError(nameof(request.Password), error.Description);
                    }
                    return this.CreateValidationError(ModelState);
                }
            }
            if (!isAdmin)
            {
                if (!await _throttleService.Throttle(ZoneLimits.Register, this.HttpContext.Connection.RemoteIpAddress, cancellationToken))
                    return new TooManyRequestsResult(ZoneLimits.Register);
            }
            var identityResult = hasPassword ? await _userManager.CreateAsync(user, request.Password) : await _userManager.CreateAsync(user);
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

            var isNewAdmin = request.IsAdministrator is true;
            if (isNewAdmin)
            {
                if (!anyAdmin)
                {
                    await _roleManager.CreateAsync(new IdentityRole(Roles.ServerAdmin));
                }
                await _userManager.AddToRoleAsync(user, Roles.ServerAdmin);
                if (!anyAdmin)
                {
                    var settings = await _settingsRepository.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();
                    if (settings.FirstRun)
                    {
                        settings.FirstRun = false;
                        await _settingsRepository.UpdateSetting(settings);
                    }
                    await _settingsRepository.FirstAdminRegistered(policies, _options.UpdateUrl != null, _options.DisableRegistration, Logs);
                }
            }
            _eventAggregator.Publish(await UserEvent.Registered.Create(user, await _userManager.GetUserAsync(User), _callbackGenerator, request.SendInvitationEmail is not false));
            var model = await ForAPI(user);
            return CreatedAtAction(string.Empty, model);
        }

        [HttpDelete("~/api/v1/users/{idOrEmail}")]
        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> DeleteUser(string idOrEmail)
        {
            var user = await _userManager.FindByIdOrEmail(idOrEmail);
            if (user is null)
            {
                return this.UserNotFound();
            }

            // We can safely delete the user if it's not an admin user
            if (!(await _userService.IsAdminUser(user)))
            {
                await _userService.DeleteUserAndAssociatedData(user);

                return Ok();
            }

            // User shouldn't be deleted if it's the only admin
            if (await _userService.IsUserTheOnlyOneAdmin(new (user, baseUrl: Request.GetRequestBaseUrl())))
            {
                return Forbid(AuthenticationSchemes.GreenfieldBasic);
            }

            // Ok, this user is an admin but there are other admins as well so safe to delete
            await _userService.DeleteUserAndAssociatedData(user);
            _eventAggregator.Publish(new UserEvent.Deleted(user));

            return Ok();
        }

        private async Task<ApplicationUserData> ForAPI(ApplicationUser data)
        {
            var roles = (await _userManager.GetRolesAsync(data)).ToArray();
            return await UserService.ForAPI<ApplicationUserData>(data, roles, _callbackGenerator, _uriResolver, Request);
        }
    }
}
