using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using static BTCPayServer.Services.Stores.StoreRepository;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreUsersController(
        StoreRepository storeRepository,
        UserManager<ApplicationUser> userManager,
        CallbackGenerator callbackGenerator,
        IAuthorizationService authorizationService,
        PoliciesSettings policiesSettings,
        EventAggregator eventAggregator,
        EmailSenderFactory emailSenderFactory)
        : ControllerBase
    {
        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/users")]
        public async Task<IActionResult> GetStoreUsers(string storeId)
            => Ok((await storeRepository.GetStoreUsers(storeId))
                .Select(u => new StoreUserData()
                {
                    Id = u.Id,
                    Email = u.Email,
                    RoleId = u.StoreRole.Id,
                }));

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/users/invitations")]
        public async Task<IActionResult> GetStoreInvitations(string storeId)
            => Ok((await storeRepository.GetStoreInvitations(storeId)).Select(ToStoreInvitationData));

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/users/{idOrEmail}/invitation")]
        public async Task<IActionResult> ResendStoreInvitation(string storeId, string idOrEmail)
        {
            var user = await userManager.FindByIdOrEmail(idOrEmail);
            if (user is null)
                return UserNotFound();
            var invitation = await storeRepository.ResendStoreInvitation(storeId, user.Id);
            if (invitation is null)
                return StoreInvitationNotFound();
            return Ok(await NotifyStoreInvitation(invitation));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/users/{idOrEmail}/invitation")]
        public async Task<IActionResult> CancelStoreInvitation(string storeId, string idOrEmail)
        {
            var user = await userManager.FindByIdOrEmail(idOrEmail);
            if (user is null)
                return UserNotFound();
            return await storeRepository.DeleteStoreInvitation(storeId, user.Id)
                ? Ok()
                : StoreInvitationNotFound();
        }

        [Authorize(Policy = Policies.CanModifyProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/invitations/{token}")]
        public async Task<IActionResult> GetStoreInvitation(string token)
        {
            var invitation = await storeRepository.GetStoreInvitationByToken(token, userId: User.GetId());
            return invitation is null? StoreInvitationNotFound()
                : Ok(ToStoreInvitationData(invitation));
        }

        [Authorize(Policy = Policies.CanModifyProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/invitations/{token}")]
        public async Task<IActionResult> DeclineStoreInvitation(string token)
            => await storeRepository.DeleteStoreInvitationByToken(User.GetId(), token)
                ? Ok()
                : StoreInvitationNotFound();

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/users/{idOrEmail}")]
        public async Task<IActionResult> RemoveStoreUser(string storeId, string idOrEmail)
        {
            var user = await userManager.FindByIdOrEmail(idOrEmail);
            if (user == null)
                return UserNotFound();

            return await storeRepository.RemoveStoreUser(storeId, user.Id)
                ? Ok()
                : this.CreateAPIError(409, "store-user-role-orphaned", "Removing this user would result in the store having no owner.");
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/users")]
        public async Task<IActionResult> AddStoreUser(string storeId, AddStoreUserDataRequest request)
        {
            var requireInvitation = request.RequireInvitation ?? true;
            if (!requireInvitation && !(await authorizationService.AuthorizeAsync(User, null, Policies.CanModifyServerSettings)).Succeeded)
                return this.CreateAPIPermissionError(Policies.CanModifyServerSettings, "You are not allowed to add users without invitation");

            var (error, user, roleId) = await GetStoreUserRequest(storeId, request, null, createUser: true);
            if (error is not null)
                return error;

            var storeUser = await storeRepository.GetStoreUser(storeId, user.Id);
            if (storeUser is not null)
                return AlreadyStoreUser();

            if (!requireInvitation)
            {
                return await storeRepository.AddStoreUser(storeId, user.Id, roleId)
                    ? Ok(new AddStoreUserResult())
                    : this.CreateAPIError(409, "duplicate-store-user-role", "The user is already added to the store");
            }
            else
            {
                var res = await storeRepository.CreateStoreInvitation(storeId, user.Id, roleId, User.GetId());
                return res switch
                {
                    CreateStoreInvitationResult.AlreadyMember => AlreadyStoreUser(),
                    CreateStoreInvitationResult.InvalidRole => this.CreateAPIError(400, "invalid-role", "The role is invalid"),
                    CreateStoreInvitationResult.Success s => Ok(new AddStoreUserResult { StoreInvitation = await NotifyStoreInvitation(s.Invitation) }),
                    _ => throw new InvalidOperationException(res.ToString())
                };
            }
        }

        private IActionResult AlreadyStoreUser() => this.CreateAPIError(409, "already-store-user", "The user is already added to the store");

        [Authorize(Policy = Policies.CanModifyProfile, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/invitations/{token}")]
        public async Task<IActionResult> AcceptStoreInvitation(string token)
        {
            var res = await storeRepository.AcceptStoreInvitation(User.GetId(), token);
            return res switch
            {
                AddOrUpdateStoreUserResult.Success => Ok(),
                AddOrUpdateStoreUserResult.DuplicateRole => AlreadyStoreUser(),
                AddOrUpdateStoreUserResult.Expired => this.CreateAPIError(410, "store-invitation-expired", "The invitation has expired"),
                AddOrUpdateStoreUserResult.InvalidRole => this.CreateAPIError(400, "invalid-role", "The role is invalid"),
                null => this.CreateAPIError(404, "store-invitation-not-found", "The invitation was not found"),
                _ => throw new InvalidOperationException(res.ToString())
            };
        }

        private IActionResult StoreInvitationNotFound()
            => this.CreateAPIError(404, "store-invitation-not-found", "The invitation was not found");

        private StoreInvitationData ToStoreInvitationData(StoreInvitationExtendedRow i)
            => new()
            {
                StoreId = i.StoreId,
                StoreName = i.StoreName,
                UserId = i.UserId,
                UserEmail = i.UserEmail,
                RoleId = i.RoleId,
                InvitedByUserId = i.InvitedByUserId,
                Created = i.Created,
                ExpiresAt = i.ExpiresAt,
                IsExpired = i.IsExpired(),
                IsForCurrentUser = i.UserId == User.GetId()
            };

        private async Task<AddStoreUserResult.InvitationResult> NotifyStoreInvitation(GeneratedInvitation invitation)
        {
            var link = callbackGenerator.StoreInvitationLink(invitation.Token);
            await storeRepository.NotifyStoreInvitation(invitation.Invitation, link);
            return new AddStoreUserResult.InvitationResult { Token = invitation.Token, Link = link };
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/users/{idOrEmail?}")]
        public async Task<IActionResult> UpdateStoreUser(string storeId, StoreUserDataRequest request, string idOrEmail = null)
        {
            var (error, user, roleId) = await GetStoreUserRequest(storeId, request, idOrEmail);
            if (error is not null)
                return error;
            var res = await storeRepository.UpdateStoreUserRole(storeId, user.Id, roleId);
            return res switch
            {
                AddOrUpdateStoreUserResult.Success => Ok(),
                AddOrUpdateStoreUserResult.DuplicateRole _ => Ok(),
                _ => this.CreateAPIError(409, "store-user-role-orphaned", "Removing this user would result in the store having no owner."),
            };
        }

        private async Task<(IActionResult error, ApplicationUser user, StoreRoleId roleId)> GetStoreUserRequest(string storeId, StoreUserDataRequest request,
            string idOrEmail, bool createUser = false)
        {
            // Deprecated properties
            request.StoreRole ??= request.AdditionalData.TryGetValue("role", out var role) ? role.ToString() : null;
            request.Id ??= request.AdditionalData.TryGetValue("userId", out var userId) ? userId.ToString() : null;

            StoreRoleId roleId = null;
            if (request.StoreRole is not null)
            {
                roleId = await storeRepository.ResolveStoreRoleId(storeId, request.StoreRole);
                if (roleId is null)
                    ModelState.AddModelError(nameof(request.StoreRole), "The role id provided does not exist");
            }

            if (!ModelState.IsValid)
                return (this.CreateValidationError(ModelState), null, null);

            var id = idOrEmail ?? request.Id;
            var user = await userManager.FindByIdOrEmail(id);
            if (user is null && createUser && MailboxAddressValidator.IsMailboxAddress(id))
            {
                if (policiesSettings.LockSubscription &&
                    !(await authorizationService.AuthorizeAsync(User, null, new PolicyRequirement(Policies.CanCreateUser))).Succeeded)
                    return (this.CreateAPIPermissionError(Policies.CanCreateUser), null, null);

                user = new ApplicationUser
                {
                    UserName = id,
                    Email = id,
                    RequiresEmailConfirmation = policiesSettings.RequiresConfirmedEmail,
                    RequiresApproval = policiesSettings.RequiresUserApproval,
                    Created = DateTimeOffset.UtcNow
                };
                var creation = await userManager.CreateAsync(user);
                if (!creation.Succeeded)
                {
                    foreach (var identityError in creation.Errors)
                        ModelState.AddModelError(nameof(request.Id), identityError.Description);
                    return (this.CreateValidationError(ModelState), null, null);
                }

                var currentUser = await userManager.GetUserAsync(User);
                if (currentUser is not null)
                {
                    var evt = await UserEvent.Registered.Create(user, currentUser, callbackGenerator, await emailSenderFactory.IsComplete());
                    eventAggregator.Publish(evt);
                }
            }
            if (user is null)
                return (UserNotFound(), null, null);
            request.Id = user.Id;

            return (null, user, roleId);
        }

        private IActionResult UserNotFound() => this.CreateAPIError(404, "user-not-found", "The user was not found");
    }
}
