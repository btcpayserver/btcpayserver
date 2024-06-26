#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    [HttpGet("{storeId}/users")]
    public async Task<IActionResult> StoreUsers()
    {
        var vm = new StoreUsersViewModel { Role = StoreRoleId.Employee.Role };
        await FillUsers(vm);
        return View(vm);
    }

    [HttpPost("{storeId}/users")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> StoreUsers(string storeId, StoreUsersViewModel vm)
    {
        await FillUsers(vm);
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var roles = await _storeRepo.GetStoreRoles(CurrentStore.Id);
        if (roles.All(role => role.Id != vm.Role))
        {
            ModelState.AddModelError(nameof(vm.Role), "Invalid role");
            return View(vm);
        }
            
        var user = await _userManager.FindByEmailAsync(vm.Email);
        var isExistingUser = user is not null;
        var isExistingStoreUser = isExistingUser && await _storeRepo.GetStoreUser(storeId, user!.Id) is not null;
        var successInfo = string.Empty;
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = vm.Email,
                Email = vm.Email,
                RequiresEmailConfirmation = _policiesSettings.RequiresConfirmedEmail,
                RequiresApproval = _policiesSettings.RequiresUserApproval,
                Created = DateTimeOffset.UtcNow
            };

            var result = await _userManager.CreateAsync(user);
            if (result.Succeeded)
            {
                var tcs = new TaskCompletionSource<Uri>();
                var currentUser = await _userManager.GetUserAsync(HttpContext.User);

                _eventAggregator.Publish(new UserRegisteredEvent
                {
                    RequestUri = Request.GetAbsoluteRootUri(),
                    Kind = UserRegisteredEventKind.Invite,
                    User = user,
                    InvitedByUser = currentUser,
                    CallbackUrlGenerated = tcs
                });
                    
                var callbackUrl = await tcs.Task;
                var settings = await _settingsRepository.GetSettingAsync<EmailSettings>() ?? new EmailSettings();
                var info = settings.IsComplete()
                    ? "An invitation email has been sent.<br/>You may alternatively"
                    : "An invitation email has not been sent, because the server does not have an email server configured.<br/> You need to";
                successInfo = $"{info} share this link with them: <a class='alert-link' href='{callbackUrl}'>{callbackUrl}</a>";
            }
            else
            {
                ModelState.AddModelError(nameof(vm.Email), "User could not be invited");
                return View(vm);
            }
        }

        var roleId = await _storeRepo.ResolveStoreRoleId(storeId, vm.Role);
        var action = isExistingUser
            ? isExistingStoreUser ? "updated" : "added"
            : "invited";
        if (await _storeRepo.AddOrUpdateStoreUser(CurrentStore.Id, user.Id, roleId))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                AllowDismiss = false,
                Html = $"User {action} successfully." + (string.IsNullOrEmpty(successInfo) ? "" : $" {successInfo}")
            });
            return RedirectToAction(nameof(StoreUsers));
        }

        ModelState.AddModelError(nameof(vm.Email), $"The user could not be {action}");
        return View(vm);
    }

    [HttpPost("{storeId}/users/{userId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> UpdateStoreUser(string storeId, string userId, StoreUsersViewModel.StoreUserViewModel vm)
    {
        var roleId = await _storeRepo.ResolveStoreRoleId(storeId, vm.Role);
        var storeUsers = await _storeRepo.GetStoreUsers(storeId);
        var user = storeUsers.First(user => user.Id == userId);
        var isOwner = user.StoreRole.Id == StoreRoleId.Owner.Id;
        var isLastOwner = isOwner && storeUsers.Count(u => u.StoreRole.Id == StoreRoleId.Owner.Id) == 1;
        if (isLastOwner && roleId != StoreRoleId.Owner)
            TempData[WellKnownTempData.ErrorMessage] = $"User {user.Email} is the last owner. Their role cannot be changed.";
        else if (await _storeRepo.AddOrUpdateStoreUser(storeId, userId, roleId))
            TempData[WellKnownTempData.SuccessMessage] = $"The role of {user.Email} has been changed to {vm.Role}.";
        return RedirectToAction(nameof(StoreUsers), new { storeId, userId });
    }

    [HttpPost("{storeId}/users/{userId}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteStoreUser(string storeId, string userId)
    {
        if (await _storeRepo.RemoveStoreUser(storeId, userId))
            TempData[WellKnownTempData.SuccessMessage] = "User removed successfully.";
        else
            TempData[WellKnownTempData.ErrorMessage] = "Removing this user would result in the store having no owner.";
        return RedirectToAction(nameof(StoreUsers), new { storeId, userId });
    }

    private async Task FillUsers(StoreUsersViewModel vm)
    {
        var users = await _storeRepo.GetStoreUsers(CurrentStore.Id);
        vm.StoreId = CurrentStore.Id;
        vm.Users = users.Select(u => new StoreUsersViewModel.StoreUserViewModel
        {
            Email = u.Email,
            Name = u.UserBlob.Name,
            ImageUrl = u.UserBlob.ImageUrl,
            Id = u.Id,
            Role = u.StoreRole.Role
        }).ToList();
    }
}
