using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Controllers
{
    public partial class UIServerController
    {
        [HttpGet("server/users")]
        public async Task<IActionResult> ListUsers(
            [FromServices] RoleManager<IdentityRole> roleManager,
            UsersViewModel model,
            string sortOrder = null)
        {
            model = this.ParseListQuery(model ?? new UsersViewModel());

            var usersQuery = _UserManager.Users;
            if (!string.IsNullOrWhiteSpace(model.SearchTerm))
            {
#pragma warning disable CA1307 // Specify StringComparison
                // Entity Framework don't support StringComparison
                usersQuery = usersQuery.Where(u => u.Email.Contains(model.SearchTerm));
#pragma warning restore CA1307 // Specify StringComparison
            }

            if (sortOrder != null)
            {
                switch (sortOrder)
                {
                    case "desc":
                        ViewData["NextUserEmailSortOrder"] = "asc";
                        usersQuery = usersQuery.OrderByDescending(user => user.Email);
                        break;
                    case "asc":
                        usersQuery = usersQuery.OrderBy(user => user.Email);
                        ViewData["NextUserEmailSortOrder"] = "desc";
                        break;
                }
            }

            model.Roles = roleManager.Roles.ToDictionary(role => role.Id, role => role.Name);
            model.Users = (await usersQuery
                .Include(user => user.UserRoles)
                .Include(user => user.UserStores)
                .ThenInclude(data => data.StoreData)
                .Skip(model.Skip)
                .Take(model.Count)
                .ToListAsync())
                .Select(u =>
                {
                    var blob = u.GetBlob();
                    return new UsersViewModel.UserViewModel
                    {
                        Name = blob?.Name,
                        ImageUrl = blob?.ImageUrl,
                        Email = u.Email,
                        Id = u.Id,
                        InvitationUrl =
                            string.IsNullOrEmpty(blob?.InvitationToken)
                                ? null
                                : _callbackGenerator.ForInvitation(u.Id, blob.InvitationToken),
                        EmailConfirmed = u.RequiresEmailConfirmation ? u.EmailConfirmed : null,
                        Approved = u.RequiresApproval ? u.Approved : null,
                        Created = u.Created,
                        Roles = u.UserRoles.Select(role => role.RoleId),
                        Disabled = u.IsDisabled,
                        Stores = u.UserStores.OrderBy(s => !s.StoreData.Archived).ToList()
                    };
                })
                .ToList();
            return View(model);
        }

        [HttpGet("server/users/{userId}")]
        public new async Task<IActionResult> User(string userId)
        {
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            var roles = await _UserManager.GetRolesAsync(user);
            var blob = user.GetBlob();
            var model = new UsersViewModel.UserViewModel
            {
                Id = user.Id,
                Email = user.Email,
                Name = blob?.Name,
                InvitationUrl = string.IsNullOrEmpty(blob?.InvitationToken) ? null : _callbackGenerator.ForInvitation(user.Id, blob.InvitationToken),
                ImageUrl = string.IsNullOrEmpty(blob?.ImageUrl) ? null : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), UnresolvedUri.Create(blob.ImageUrl)),
                EmailConfirmed = user.RequiresEmailConfirmation ? user.EmailConfirmed : null,
                Approved = user.RequiresApproval ? user.Approved : null,
                IsAdmin = Roles.HasServerAdmin(roles)
            };
            return View(model);
        }

        [HttpPost("server/users/{userId}")]
        public new async Task<IActionResult> User(string userId, UsersViewModel.UserViewModel viewModel, [FromForm] bool RemoveImageFile = false)
        {
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            bool? propertiesChanged = null;
            bool? adminStatusChanged = null;
            bool? approvalStatusChanged = null;

            if (user.RequiresApproval && viewModel.Approved.HasValue && user.Approved != viewModel.Approved.Value)
            {
                var loginLink = _callbackGenerator.ForLogin(user);
                approvalStatusChanged = await _userService.SetUserApproval(user.Id, viewModel.Approved.Value, loginLink);
            }
            if (user.RequiresEmailConfirmation && viewModel.EmailConfirmed.HasValue && user.EmailConfirmed != viewModel.EmailConfirmed)
            {
                user.EmailConfirmed = viewModel.EmailConfirmed.Value;
                propertiesChanged = true;
            }

            var blob = user.GetBlob() ?? new();
            if (blob.Name != viewModel.Name)
            {
                blob.Name = viewModel.Name;
                propertiesChanged = true;
            }

            if (viewModel.ImageFile != null)
            {
                var imageUpload = await _fileService.UploadImage(viewModel.ImageFile, user.Id);
                if (!imageUpload.Success)
                    ModelState.AddModelError(nameof(viewModel.ImageFile), imageUpload.Response);
                else
                {
                    try
                    {
                        var storedFile = imageUpload.StoredFile!;
                        var fileIdUri = new UnresolvedUri.FileIdUri(storedFile.Id);
                        blob.ImageUrl = fileIdUri.ToString();
                        propertiesChanged = true;
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError(nameof(viewModel.ImageFile), StringLocalizer["Could not save image: {0}", e.Message]);
                    }
                }
            }
            else if (RemoveImageFile && !string.IsNullOrEmpty(blob.ImageUrl))
            {
                blob.ImageUrl = null;
                propertiesChanged = true;
            }
            user.SetBlob(blob);
            var admins = await _UserManager.GetUsersInRoleAsync(Roles.ServerAdmin);
            var roles = await _UserManager.GetRolesAsync(user);
            var wasAdmin = Roles.HasServerAdmin(roles);
            if (!viewModel.IsAdmin && admins.Count == 1 && wasAdmin)
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["This is the only admin, so their role can't be removed until another admin is added."].Value;
                return View(viewModel);
            }

            if (viewModel.IsAdmin != wasAdmin)
            {
                adminStatusChanged = await _userService.SetAdminUser(user.Id, viewModel.IsAdmin);
            }

            if (propertiesChanged is true)
            {
                propertiesChanged = await _UserManager.UpdateAsync(user) is { Succeeded: true };
            }

            if (propertiesChanged.HasValue || adminStatusChanged.HasValue || approvalStatusChanged.HasValue)
            {
                if (propertiesChanged is not false && adminStatusChanged is not false && approvalStatusChanged is not false)
                {
                    TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["User successfully updated"].Value;
                }
                else
                {
                    TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Error updating user"].Value;
                }
            }

            return RedirectToAction(nameof(User), new { userId });
        }

        [HttpGet("server/users/{userId}/reset-password")]
        public async Task<IActionResult> ResetUserPassword(string userId)
        {
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            return View(new ResetUserPasswordFromAdmin { Email = user.Email });
        }

        [HttpPost("server/users/{userId}/reset-password")]
        public async Task<IActionResult> ResetUserPassword(string userId, ResetUserPasswordFromAdmin model)
        {

            var user = await _UserManager.FindByEmailAsync(model.Email);
            if (user == null || user.Id != userId)
                return NotFound();

            var result = await _UserManager.ResetPasswordAsync(user, await _UserManager.GeneratePasswordResetTokenAsync(user), model.Password);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = result.Succeeded ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Error,
                Message = result.Succeeded ? StringLocalizer["Password successfully set"].Value : StringLocalizer["An error occurred while resetting user password"].Value
            });
            return RedirectToAction(nameof(ListUsers));
        }

        [HttpGet("server/users/new")]
        public async Task<IActionResult> CreateUser()
        {
            await PrepareCreateUserViewData();
            var vm = new RegisterFromAdminViewModel
            {
                SendInvitationEmail = ViewData["CanSendEmail"] is true
            };
            return View(vm);
        }

        [HttpPost("server/users/new")]
        public async Task<IActionResult> CreateUser(RegisterFromAdminViewModel model)
        {
            await PrepareCreateUserViewData();
            if (!_Options.CheatMode)
                model.IsAdmin = false;
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    EmailConfirmed = model.EmailConfirmed,
                    RequiresEmailConfirmation = _policiesSettings.RequiresConfirmedEmail,
                    RequiresApproval = _policiesSettings.RequiresUserApproval,
                    Approved = true, // auto-approve users created by an admin
                    Created = DateTimeOffset.UtcNow
                };

                var result = string.IsNullOrEmpty(model.Password)
                    ? await _UserManager.CreateAsync(user)
                    : await _UserManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    if (model.IsAdmin && !(await _UserManager.AddToRoleAsync(user, Roles.ServerAdmin)).Succeeded)
                        model.IsAdmin = false;

                    var currentUser = await _UserManager.GetUserAsync(HttpContext.User);
                    var sendEmail = model.SendInvitationEmail && ViewData["CanSendEmail"] is true;

                    var evt = (UserEvent.Invited)await UserEvent.Registered.Create(user, currentUser, _callbackGenerator, sendEmail);
                    _eventAggregator.Publish(evt);

                    var info = sendEmail
                        ? "An invitation email has been sent. You may alternatively"
                        : "An invitation email has not been sent. You need to";

                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        AllowDismiss = false,
                        Html = $"Account successfully created. {info} share this link with them:<br/>{evt.InvitationLink}"
                    });
                    return RedirectToAction(nameof(User), new { userId = user.Id });
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [HttpGet("server/users/{userId}/delete")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var roles = await _UserManager.GetRolesAsync(user);
            if (Roles.HasServerAdmin(roles))
            {
                var loginContext = CreateLoginContext(user);
                if (await _userService.IsUserTheOnlyOneAdmin(loginContext))
                {
                    return View("Confirm", new ConfirmModel(StringLocalizer["Delete admin"],
                        $"Unable to proceed: As the user <strong>{Html.Encode(user.Email)}</strong> is the last enabled admin, it cannot be removed."));
                }

                return View("Confirm", new ConfirmModel(StringLocalizer["Delete admin"],
                    StringLocalizer["The admin {0} will be permanently deleted. This action will also delete all accounts, users and data associated with the server account. Are you sure?", Html.Encode(user.Email)],
                    StringLocalizer["Delete"]));
            }

            return View("Confirm", new ConfirmModel(StringLocalizer["Delete user"], $"The user <strong>{Html.Encode(user.Email)}</strong> will be permanently deleted. Are you sure?", StringLocalizer["Delete"]));
        }

        [HttpPost("server/users/{userId}/delete")]
        public async Task<IActionResult> DeleteUserPost(string userId)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            await _userService.DeleteUserAndAssociatedData(user);

            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["User deleted"].Value;
            return RedirectToAction(nameof(ListUsers));
        }

        [HttpGet("server/users/{userId}/toggle")]
        public async Task<IActionResult> ToggleUser(string userId, bool enable)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var loginContext = CreateLoginContext(user);
            if (!enable && await _userService.IsUserTheOnlyOneAdmin(loginContext))
            {
                return View("Confirm", new ConfirmModel(StringLocalizer["Disable admin"],
                    $"Unable to proceed: As the user <strong>{Html.Encode(user.Email)}</strong> is the last enabled admin, it cannot be disabled."));
            }
            return View("Confirm", new ConfirmModel($"{(enable ? "Enable" : "Disable")} user", $"The user <strong>{Html.Encode(user.Email)}</strong> will be {(enable ? "enabled" : "disabled")}. Are you sure?", (enable ? StringLocalizer["Enable"] : StringLocalizer["Disable"])));
        }

        [HttpPost("server/users/{userId}/toggle")]
        public async Task<IActionResult> ToggleUserPost(string userId, bool enable)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            var loginContext = CreateLoginContext(user);
            if (!enable && await _userService.IsUserTheOnlyOneAdmin(loginContext))
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["User was the last enabled admin and could not be disabled."].Value;
                return RedirectToAction(nameof(ListUsers));
            }
            await _userService.SetDisabled(userId, !enable);

            TempData[WellKnownTempData.SuccessMessage] = enable
                ? StringLocalizer["User enabled"].Value
                : StringLocalizer["User disabled"].Value;
            return RedirectToAction(nameof(ListUsers));
        }

        private UserService.CanLoginContext CreateLoginContext(ApplicationUser user)
        {
            return new UserService.CanLoginContext(user, StringLocalizer, ViewLocalizer, Request.GetRequestBaseUrl());
        }

        [HttpGet("server/users/{userId}/approve")]
        public async Task<IActionResult> ApproveUser(string userId, bool approved)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            return View("Confirm", new ConfirmModel($"{(approved ? StringLocalizer["Approve"] : StringLocalizer["Unapprove"])} user", $"The user <strong>{Html.Encode(user.Email)}</strong> will be {(approved ? "approved" : "unapproved")}. Are you sure?", (approved ? StringLocalizer["Approve"] : StringLocalizer["Unapprove"])));
        }

        [HttpPost("server/users/{userId}/approve")]
        public async Task<IActionResult> ApproveUserPost(string userId, bool approved)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var loginLink = _callbackGenerator.ForLogin(user);
            await _userService.SetUserApproval(userId, approved, loginLink);

            TempData[WellKnownTempData.SuccessMessage] = approved
                ? StringLocalizer["User approved"].Value
                : StringLocalizer["User unapproved"].Value;
            return RedirectToAction(nameof(ListUsers));
        }

        [HttpGet("server/users/{userId}/verification-email")]
        public async Task<IActionResult> SendVerificationEmail(string userId)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            return View("Confirm", new ConfirmModel(StringLocalizer["Send verification email"], $"This will send a verification email to <strong>{Html.Encode(user.Email)}</strong>.", StringLocalizer["Send"]));
        }

        [HttpPost("server/users/{userId}/verification-email")]
        public async Task<IActionResult> SendVerificationEmailPost(string userId)
        {
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{userId}'.");
            }

            var callbackUrl = await _callbackGenerator.ForEmailConfirmation(user);
            _eventAggregator.Publish(new UserEvent.ConfirmationEmailRequested(user, callbackUrl));
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Verification email sent"].Value;
            return RedirectToAction(nameof(ListUsers));
        }

        private async Task PrepareCreateUserViewData()
        {
            ViewData["CanSendEmail"] = await _emailSenderFactory.IsComplete();
            ViewData["AllowRequestEmailConfirmation"] = _policiesSettings.RequiresConfirmedEmail;
        }
    }

    public class ResetUserPasswordFromAdmin
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; }
    }

    public class RegisterFromAdminViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; }

        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password (leave blank to generate invite-link)")]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; }

        [Display(Name = "Is administrator?")]
        public bool IsAdmin { get; set; }

        [Display(Name = "Email confirmed?")]
        public bool EmailConfirmed { get; set; }

        [Display(Name = "Send invitation email")]
        public bool SendInvitationEmail { get; set; } = true;
    }
}
