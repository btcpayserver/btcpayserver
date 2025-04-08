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
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using NicolasDorier.RateLimits;
using static BTCPayServer.Services.Stores.StoreRepository;

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

    enum StoreUsersAction
    {
        Added,
        Updated,
        Invited
    }
    [HttpPost("{storeId}/users")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [RateLimitsFilter(ZoneLimits.Register, Scope = RateLimitsScope.RemoteAddress)]
    public async Task<IActionResult> StoreUsers(string storeId, StoreUsersViewModel vm)
    {
        await FillUsers(vm);
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var roleId = await _storeRepo.ResolveStoreRoleId(storeId, vm.Role);
        if (roleId is null)
        {
            ModelState.AddModelError(nameof(vm.Role), StringLocalizer["Invalid role"]);
            return View(vm);
        }

        StoreUsersAction action;
        string? inviteInfo = null;
        var user = await _userManager.FindByEmailAsync(vm.Email);
        if (user is null)
        {
            action = StoreUsersAction.Invited;
            if (!_policiesSettings.LockSubscription || await IsAdmin())
            {
                user = new ApplicationUser
                {
                    UserName = vm.Email,
                    Email = vm.Email,
                    RequiresEmailConfirmation = _policiesSettings.RequiresConfirmedEmail,
                    RequiresApproval = _policiesSettings.RequiresUserApproval,
                    Created = DateTimeOffset.UtcNow
                };
                var currentUser = await _userManager.GetUserAsync(HttpContext.User);

                if (currentUser is not null &&
                    (await _userManager.CreateAsync(user)) is { Succeeded: true } result)
                {
                    var invitationEmail = await _emailSenderFactory.IsComplete();
                    var evt = await UserEvent.Invited.Create(user!, currentUser, _callbackGenerator, Request, invitationEmail);
                    _eventAggregator.Publish(evt);
                    inviteInfo = invitationEmail
                        ? StringLocalizer["An invitation email has been sent.<br/>You may alternatively share this link with them: <a class='alert-link' href='{0}'>{0}</a>", evt.InvitationLink]
                        : StringLocalizer["An invitation email has not been sent, because the server does not have an email server configured.<br/> You need to share this link with them: <a class='alert-link' href='{0}'>{0}</a>", evt.InvitationLink];
                    user = await _userManager.FindByEmailAsync(vm.Email);
                }
            }
        }
        else
        {
            action = (await _storeRepo.GetStoreUser(storeId, user.Id)) is not null
                 ? StoreUsersAction.Updated
                 : StoreUsersAction.Added;
        }

        if (user is null)
        {
            ModelState.AddModelError(nameof(vm.Email), StringLocalizer["User not found"]);
            return View(vm);
        }

        var res = await _storeRepo.AddOrUpdateStoreUser(CurrentStore.Id, user.Id, roleId);
        if (res is AddOrUpdateStoreUserResult.Success)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                AllowDismiss = false,
                Message = action switch
                {
                    StoreUsersAction.Added => StringLocalizer["The user has been added successfully."].Value,
                    StoreUsersAction.Updated => StringLocalizer["The user has been updated successfully."].Value,
                    StoreUsersAction.Invited => null,
                    _ => throw new ArgumentOutOfRangeException(action.ToString())
                },
                Html = action switch
                {
                    StoreUsersAction.Invited => inviteInfo,
                    _ => null
                }
            });
            return RedirectToAction(nameof(StoreUsers));
        }
        else
        {
            ModelState.AddModelError(nameof(vm.Email),
                action switch
                {
                    StoreUsersAction.Updated => StringLocalizer["The user could not be updated: {0}", res.ToString()],
                    StoreUsersAction.Added => StringLocalizer["The user could not be added: {0}", res.ToString()],
                    StoreUsersAction.Invited => StringLocalizer["The user could not be invited: {0}", res.ToString()],
                    _ => throw new ArgumentOutOfRangeException(action.ToString())
                });
            return View(vm);
        }
    }

    private async Task<bool> IsAdmin()
    => (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanCreateUser))).Succeeded;

    [HttpPost("{storeId}/users/{userId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> UpdateStoreUser(string storeId, string userId, StoreUsersViewModel.StoreUserViewModel vm)
    {
        var roleId = await _storeRepo.ResolveStoreRoleId(storeId, vm.Role);
        var storeUsers = await _storeRepo.GetStoreUsers(storeId);
        var user = storeUsers.First(user => user.Id == userId);

        var res = await _storeRepo.AddOrUpdateStoreUser(storeId, userId, roleId);
        if (res is AddOrUpdateStoreUserResult.Success)
        {
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The role of {0} has been changed to {1}.", user.Email, vm.Role].Value;
        }
        else
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Changing the role of user {0} failed: {1}", user.Email, res.ToString()].Value;
        }
        return RedirectToAction(nameof(StoreUsers), new { storeId, userId });
    }

    [HttpPost("{storeId}/users/{userId}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteStoreUser(string storeId, string userId)
    {
        if (await _storeRepo.RemoveStoreUser(storeId, userId))
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["User removed successfully."].Value;
        else
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Removing this user would result in the store having no owner."].Value;
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
