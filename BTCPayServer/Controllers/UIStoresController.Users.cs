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
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.Controllers;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        if (vm.Command == "Add")
        {
            string? serverInviteInfo = null;
            bool newAccount = false;
            var user = await _userManager.FindByEmailAsync(vm.Email);
            if (user is null)
            {
                if (await CanCreateUser())
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
                        (await _userManager.CreateAsync(user)) is { Succeeded: true })
                    {
                        var invitationEmail = await _emailSenderFactory.IsComplete();
                        var evt = (UserEvent.Invited)await UserEvent.Registered.Create(user!, currentUser, _callbackGenerator, invitationEmail);
                        _eventAggregator.Publish(evt);
                        var emailSettingsLink = await CanModifyServerSettings() ? EmailSettingsLink() : null;
                        serverInviteInfo = invitationEmail
                            ? StringLocalizer["An invitation email has been sent.<br/>You may alternatively share this link with them: <a class='alert-link' href='{0}'>{0}</a>", evt.InvitationLink]
                            : emailSettingsLink is null
                                ? StringLocalizer["An invitation email has not been sent, because the server does not have an email server configured.<br/> You need to share this link with them: <a class='alert-link' href='{0}'>{0}</a>", evt.InvitationLink]
                                : StringLocalizer["An invitation email has not been sent, because the server does not have an <a class='alert-link' href='{1}'>email server</a> configured.<br/> You need to share this link with them: <a class='alert-link' href='{0}'>{0}</a>", evt.InvitationLink, emailSettingsLink];
                        newAccount = true;
                    }
                }
            }

            if (user is null)
            {
                ModelState.AddModelError(nameof(vm.Email), StringLocalizer["User not found"]);
                return View(vm);
            }

            var requireInvitation = !vm.CanSkipInvitation || vm.RequireInvitation;
            if (requireInvitation && !newAccount)
            {
                var invitedBy = _userManager.GetUserId(User);
                var inviteRes = await _storeRepo.CreateStoreInvitation(CurrentStore.Id, user.Id, roleId, invitedBy);
                if (inviteRes is not CreateStoreInvitationResult.Success created)
                {
                    ModelState.AddModelError(nameof(vm.Email), StringLocalizer["The user could not be invited: {0}", inviteRes.ToString()]);
                    return View(vm);
                }
                var link = _callbackGenerator.StoreInvitationLink(created.Invitation.Token);
                await _storeRepo.NotifyStoreInvitation(created.Invitation.Invitation, link);
                return View("StoreInvitationSent", new StoreInvitationSentViewModel
                {
                    StoreId = CurrentStore.Id,
                    Email = user.Email,
                    Role = roleId.Role,
                    InvitationUrl = link,
                    EmailSent = await _emailSenderFactory.IsComplete(),
                    Expiry = created.Invitation.Invitation.ExpiresAt,
                    JustSent = true
                });
            }
            else
            {
                // It might fail, but this is a harmless corner case
                await _storeRepo.AddStoreUser(CurrentStore.Id, user.Id, roleId);
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    AllowDismiss = false,
                    Message = StringLocalizer["The user has been added successfully."].Value,
                    Html = serverInviteInfo
                });
                return RedirectToAction(nameof(StoreUsers));
            }
        }
        else // if (vm.Command == "Update:<UserId>")
        {
            var user = await _userManager.FindByIdAsync(vm.Command.Split(':').LastOrDefault() ?? "");
            if (user is null)
                return NotFound();
            await _storeRepo.UpdateStoreUserRole(CurrentStore.Id, user.Id, roleId);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                AllowDismiss = false,
                Message = StringLocalizer["The user has been updated successfully."].Value
            });
            return RedirectToAction(nameof(StoreUsers));
        }
    }

    /// <summary>Server email settings, which only a server admin can reach.</summary>
    private string? EmailSettingsLink()
        => Url.Action(nameof(UIServerEmailController.ServerEmailSettings), "UIServerEmail", new { area = EmailsPlugin.Area });

    private async Task<bool> CanCreateUser()
    =>  !_policiesSettings.LockSubscription || (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanCreateUser))).Succeeded;

    private async Task<bool> CanModifyServerSettings()
    => (await _authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanModifyServerSettings))).Succeeded;

    [HttpPost("{storeId}/users/{userId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> UpdateStoreUser(string storeId, string userId, StoreUsersViewModel.StoreUserViewModel vm)
    {
        var roleId = await _storeRepo.ResolveStoreRoleId(storeId, vm.Role);
        var storeUsers = await _storeRepo.GetStoreUsers(storeId);
        var user = storeUsers.FirstOrDefault(user => user.Id == userId);
        if (user is null)
            return NotFound();

        var res = await _storeRepo.UpdateStoreUserRole(storeId, userId, roleId);
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

    [HttpPost("{storeId}/users/{userId}/invitation/resend")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ResendStoreInvitation(string storeId, string userId)
    {
        var invitation = await _storeRepo.ResendStoreInvitation(storeId, userId);
        if (invitation is null)
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["There is no pending invitation for this user."].Value;
            return RedirectToAction(nameof(StoreUsers), new { storeId });
        }
        var link = _callbackGenerator.StoreInvitationLink(invitation.Token);
        await _storeRepo.NotifyStoreInvitation(invitation.Invitation, link);
        var invitedUser = await _userManager.FindByIdAsync(userId);
        return View("StoreInvitationSent", new StoreInvitationSentViewModel
        {
            StoreId = storeId,
            Email = invitedUser?.Email,
            Role = StoreRoleId.Parse(invitation.Invitation.RoleId).Role,
            InvitationUrl = link,
            EmailSent = await _emailSenderFactory.IsComplete(),
            Expiry = invitation.Invitation.ExpiresAt,
            JustSent = true
        });
    }

    [HttpPost("{storeId}/users/{userId}/invitation/cancel")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CancelStoreInvitation(string storeId, string userId)
    {
        if (await _storeRepo.DeleteStoreInvitation(storeId, userId))
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Invitation cancelled."].Value;
        else
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["There is no pending invitation for this user."].Value;
        return RedirectToAction(nameof(StoreUsers), new { storeId });
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
        var currentUserId = _userManager.GetUserId(User);
        var users = await _storeRepo.GetStoreUsers(CurrentStore.Id);
        var owners = users.Count(u => u.StoreRole.Permissions.Contains(Policies.CanModifyStoreSettings));
        vm.StoreId = CurrentStore.Id;
        vm.Users = users.Select(u => new StoreUsersViewModel.StoreUserViewModel
        {
            Email = u.Email,
            ImageUrl = u.UserBlob.ImageUrl,
            Id = u.Id,
            Role = u.StoreRole.Role,
            IsCurrentUser = u.Id == currentUserId,
            IsLocked = owners == 1 && u.StoreRole.Permissions.Contains(Policies.CanModifyStoreSettings)
        }).ToList();

        var invitations = await _storeRepo.GetStoreInvitations(CurrentStore.Id);
        vm.Users.AddRange(invitations.Select(i => new StoreUsersViewModel.StoreUserViewModel
        {
            Id = i.UserId,
            Email = i.UserEmail,
            Role = StoreRoleId.Parse(i.RoleId).Role,
            InvitedAt = i.Created,
            Expiry = i.ExpiresAt,
            IsExpired = i.IsExpired()
        }));

        vm.CanSkipInvitation = _policiesSettings.AllowStoreOwnersToSkipInvitation || await CanModifyServerSettings();
    }
}
