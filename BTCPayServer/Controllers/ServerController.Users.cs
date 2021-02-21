using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Storage.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Controllers
{
    public partial class ServerController
    {
        [Route("server/users")]
        public async Task<IActionResult> ListUsers(
            UsersViewModel model,
            string sortOrder = null
        )
        {
            model = this.ParseListQuery(model ?? new UsersViewModel());
            
            var usersQuery = _UserManager.Users;
            if (!string.IsNullOrWhiteSpace(model.SearchTerm))
            {
                usersQuery = usersQuery.Where(u => u.Email.Contains(model.SearchTerm));
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

            model.Users = await usersQuery
                .Skip(model.Skip)
                .Take(model.Count)
                .Select(u => new UsersViewModel.UserViewModel
                {
                    Name = u.UserName,
                    Email = u.Email,
                    Id = u.Id,
                    Verified = u.EmailConfirmed || !u.RequiresEmailConfirmation,
                    Created = u.Created
                })
                .ToListAsync();
            model.Total = await usersQuery.CountAsync();
             
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
                IsAdmin = IsAdmin(roles)
            };
            return View(userVM);
        }

        private static bool IsAdmin(IList<string> roles)
        {
            return roles.Contains(Roles.ServerAdmin, StringComparer.Ordinal);
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
            var wasAdmin = IsAdmin(roles);
            if (!viewModel.IsAdmin && admins.Count == 1 && wasAdmin)
            {
                TempData[WellKnownTempData.ErrorMessage] = "This is the only Admin, so their role can't be removed until another Admin is added.";
                return View(viewModel); // return
            }

            if (viewModel.IsAdmin != wasAdmin)
            {
                if (viewModel.IsAdmin)
                    await _UserManager.AddToRoleAsync(user, Roles.ServerAdmin);
                else
                    await _UserManager.RemoveFromRoleAsync(user, Roles.ServerAdmin);

                TempData[WellKnownTempData.SuccessMessage] = "User successfully updated";
            }

            return RedirectToAction(nameof(User), new { userId = userId });
        }

        [Route("server/users/new")]
        [HttpGet]
        public IActionResult CreateUser()
        {
            ViewData["AllowIsAdmin"] = _Options.AllowAdminRegistration;
            ViewData["AllowRequestEmailConfirmation"] = _cssThemeManager.Policies.RequiresConfirmedEmail;

            return View();
        }

        [Route("server/users/new")]
        [HttpPost]
        public async Task<IActionResult> CreateUser(RegisterFromAdminViewModel model)
        {
            ViewData["AllowIsAdmin"] = _Options.AllowAdminRegistration;
            ViewData["AllowRequestEmailConfirmation"] = _cssThemeManager.Policies.RequiresConfirmedEmail;
            if (!_Options.AllowAdminRegistration)
                model.IsAdmin = false;
            if (ModelState.IsValid)
            {
                IdentityResult result;
                var user = new ApplicationUser { UserName = model.Email, Email = model.Email, EmailConfirmed = model.EmailConfirmed, RequiresEmailConfirmation = _cssThemeManager.Policies.RequiresConfirmedEmail, 
                    Created = DateTimeOffset.UtcNow };

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
                        RequestUri = Request.GetAbsoluteRootUri(), User = user, Admin = model.IsAdmin is true, CallbackUrlGenerated = tcs
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
                    }else if (!await _UserManager.HasPasswordAsync(user))
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

        [Route("server/users/{userId}/delete")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var roles = await _UserManager.GetRolesAsync(user);
            if (IsAdmin(roles))
            {
                var admins = await _UserManager.GetUsersInRoleAsync(Roles.ServerAdmin);
                if (admins.Count == 1)
                {
                    // return
                    return View("Confirm", new ConfirmModel("Unable to Delete Last Admin",
                        "This is the last Admin, so it can't be removed"));
                }

                return View("Confirm", new ConfirmModel("Delete Admin " + user.Email,
                    "Are you sure you want to delete this Admin and delete all accounts, users and data associated with the server account?",
                    "Delete"));
            }
            else
            {
                return View("Confirm", new ConfirmModel("Delete user " + user.Email,
                                    "This user will be permanently deleted",
                                    "Delete"));
            }
        }

        [Route("server/users/{userId}/delete")]
        [HttpPost]
        public async Task<IActionResult> DeleteUserPost(string userId)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var files = await _StoredFileRepository.GetFiles(new StoredFileRepository.FilesQuery()
            {
                UserIds = new[] { userId },
            });

            await Task.WhenAll(files.Select(file => _FileService.RemoveFile(file.Id, userId)));

            await _UserManager.DeleteAsync(user);
            await _StoreRepository.CleanUnreachableStores();
            TempData[WellKnownTempData.SuccessMessage] = "User deleted";
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
