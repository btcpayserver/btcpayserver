using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace BTCPayServer.Controllers
{
    public partial class UIServerController
    {
        [Route("server/users")]
        public async Task<IActionResult> ListUsers(
            [FromServices] RoleManager<IdentityRole> roleManager,
        UsersViewModel model,
            string sortOrder = null
        )
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
            model.Users = await usersQuery
                .Include(user => user.UserRoles)
                .Skip(model.Skip)
                .Take(model.Count)
                .Select(u => new UsersViewModel.UserViewModel
                {
                    Name = u.UserName,
                    Email = u.Email,
                    Id = u.Id,
                    Verified = u.EmailConfirmed || !u.RequiresEmailConfirmation,
                    Created = u.Created,
                    Roles = u.UserRoles.Select(role => role.RoleId),
                    Disabled = u.LockoutEnabled && u.LockoutEnd != null && DateTimeOffset.UtcNow < u.LockoutEnd.Value.UtcDateTime
                })
                .ToListAsync();

            return View(model);
        }

        [Route("server/users/{userId}")]
        public new async Task<IActionResult> User(string userId)
        {
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            var roles = await _UserManager.GetRolesAsync(user);
            var userVM = new UsersViewModel.UserViewModel
            {
                Id = user.Id,
                Email = user.Email,
                Verified = user.EmailConfirmed || !user.RequiresEmailConfirmation,
                IsAdmin = Roles.HasServerAdmin(roles)
            };
            return View(userVM);
        }

        [Route("server/users/{userId}")]
        [HttpPost]
        public new async Task<IActionResult> User(string userId, UsersViewModel.UserViewModel viewModel)
        {
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var admins = await _UserManager.GetUsersInRoleAsync(Roles.ServerAdmin);
            var roles = await _UserManager.GetRolesAsync(user);
            var wasAdmin = Roles.HasServerAdmin(roles);
            if (!viewModel.IsAdmin && admins.Count == 1 && wasAdmin)
            {
                TempData[WellKnownTempData.ErrorMessage] = "This is the only Admin, so their role can't be removed until another Admin is added.";
                return View(viewModel); // return
            }

            if (viewModel.IsAdmin != wasAdmin)
            {
                var success = await _userService.SetAdminUser(user.Id, viewModel.IsAdmin);
                if (success)
                {
                    TempData[WellKnownTempData.SuccessMessage] = "User successfully updated";
                }
                else
                {
                    TempData[WellKnownTempData.ErrorMessage] = "Error updating user";
                }
            }

            return RedirectToAction(nameof(User), new { userId = userId });
        }

        [Route("server/users/new")]
        [HttpGet]
        public IActionResult CreateUser()
        {
            ViewData["AllowRequestEmailConfirmation"] = _policiesSettings.RequiresConfirmedEmail;
            return View();
        }

        [Route("server/users/new")]
        [HttpPost]
        public async Task<IActionResult> CreateUser(RegisterFromAdminViewModel model)
        {
            var requiresConfirmedEmail = _policiesSettings.RequiresConfirmedEmail;
            ViewData["AllowRequestEmailConfirmation"] = requiresConfirmedEmail;
            if (!_Options.CheatMode)
                model.IsAdmin = false;
            if (ModelState.IsValid)
            {
                IdentityResult result;
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    EmailConfirmed = model.EmailConfirmed,
                    RequiresEmailConfirmation = requiresConfirmedEmail,
                    Created = DateTimeOffset.UtcNow
                };

                if (!string.IsNullOrEmpty(model.Password))
                {
                    result = await _UserManager.CreateAsync(user, model.Password);
                }
                else
                {
                    result = await _UserManager.CreateAsync(user);
                }

                if (result.Succeeded)
                {
                    if (model.IsAdmin && !(await _UserManager.AddToRoleAsync(user, Roles.ServerAdmin)).Succeeded)
                        model.IsAdmin = false;

                    var tcs = new TaskCompletionSource<Uri>();

                    _eventAggregator.Publish(new UserRegisteredEvent()
                    {
                        RequestUri = Request.GetAbsoluteRootUri(),
                        User = user,
                        Admin = model.IsAdmin is true,
                        CallbackUrlGenerated = tcs
                    });
                    var callbackUrl = await tcs.Task;

                    if (user.RequiresEmailConfirmation && !user.EmailConfirmed)
                    {

                        TempData.SetStatusMessageModel(new StatusMessageModel()
                        {
                            Severity = StatusMessageModel.StatusSeverity.Success,
                            AllowDismiss = false,
                            Html =
                                $"Account created without a set password. An email will be sent (if configured) to set the password.<br/> You may alternatively share this link with them: <a class='alert-link' href='{callbackUrl}'>{callbackUrl}</a>"
                        });
                    }
                    else if (!await _UserManager.HasPasswordAsync(user))
                    {
                        TempData.SetStatusMessageModel(new StatusMessageModel()
                        {
                            Severity = StatusMessageModel.StatusSeverity.Success,
                            AllowDismiss = false,
                            Html =
                                $"Account created without a set password. An email will be sent (if configured) to set the password.<br/> You may alternatively share this link with them: <a class='alert-link' href='{callbackUrl}'>{callbackUrl}</a>"
                        });
                    }
                    return RedirectToAction(nameof(ListUsers));
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
                if (await _userService.IsUserTheOnlyOneAdmin(user))
                {
                    // return
                    return View("Confirm", new ConfirmModel("Delete admin",
                        $"Unable to proceed: As the user <strong>{Html.Encode(user.Email)}</strong> is the last enabled admin, it cannot be removed."));
                }

                return View("Confirm", new ConfirmModel("Delete admin",
                    $"The admin <strong>{Html.Encode(user.Email)}</strong> will be permanently deleted. This action will also delete all accounts, users and data associated with the server account. Are you sure?",
                    "Delete"));
            }

            return View("Confirm", new ConfirmModel("Delete user", $"The user <strong>{Html.Encode(user.Email)}</strong> will be permanently deleted. Are you sure?", "Delete"));
        }

        [HttpPost("server/users/{userId}/delete")]
        public async Task<IActionResult> DeleteUserPost(string userId)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            await _userService.DeleteUserAndAssociatedData(user);

            TempData[WellKnownTempData.SuccessMessage] = "User deleted";
            return RedirectToAction(nameof(ListUsers));
        }

        [HttpGet("server/users/{userId}/toggle")]
        public async Task<IActionResult> ToggleUser(string userId, bool enable)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            if (!enable && await _userService.IsUserTheOnlyOneAdmin(user))
            {
                return View("Confirm", new ConfirmModel("Disable admin",
                    $"Unable to proceed: As the user <strong>{Html.Encode(user.Email)}</strong> is the last enabled admin, it cannot be disabled."));
            }
            return View("Confirm", new ConfirmModel($"{(enable ? "Enable" : "Disable")} user", $"The user <strong>{Html.Encode(user.Email)}</strong> will be {(enable ? "enabled" : "disabled")}. Are you sure?", (enable ? "Enable" : "Disable")));
        }

        [HttpPost("server/users/{userId}/toggle")]
        public async Task<IActionResult> ToggleUserPost(string userId, bool enable)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            if (!enable && await _userService.IsUserTheOnlyOneAdmin(user))
            {
                TempData[WellKnownTempData.SuccessMessage] = $"User was the last enabled admin and could not be disabled.";
                return RedirectToAction(nameof(ListUsers));
            }
            await _userService.ToggleUser(userId, enable ? null : DateTimeOffset.MaxValue);

            TempData[WellKnownTempData.SuccessMessage] = $"User {(enable ? "enabled" : "disabled")}";
            return RedirectToAction(nameof(ListUsers));
        }

        [HttpGet("server/users/{userId}/verification-email")]
        public async Task<IActionResult> SendVerificationEmail(string userId)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            return View("Confirm", new ConfirmModel("Send verification email", $"This will send a verification email to <strong>{Html.Encode(user.Email)}</strong>.", "Send"));
        }

        [HttpPost("server/users/{userId}/verification-email")]
        public async Task<IActionResult> SendVerificationEmailPost(string userId)
        {
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
            {
                throw new ApplicationException($"Unable to load user with ID '{userId}'.");
            }

            var code = await _UserManager.GenerateEmailConfirmationTokenAsync(user);
            var callbackUrl = _linkGenerator.EmailConfirmationLink(user.Id, code, Request.Scheme, Request.Host, Request.PathBase);

            (await _emailSenderFactory.GetEmailSender()).SendEmailConfirmation(user.GetMailboxAddress(), callbackUrl);

            TempData[WellKnownTempData.SuccessMessage] = "Verification email sent";
            return RedirectToAction(nameof(ListUsers));
        }
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
    }
}
