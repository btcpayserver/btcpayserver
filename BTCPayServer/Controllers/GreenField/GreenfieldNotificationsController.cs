#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NotificationData = BTCPayServer.Client.Models.NotificationData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldNotificationsController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationManager _notificationManager;
        private readonly IEnumerable<INotificationHandler> _notificationHandlers;

        public GreenfieldNotificationsController(
            UserManager<ApplicationUser> userManager,
            NotificationManager notificationManager,
            IEnumerable<INotificationHandler> notificationHandlers)
        {
            _userManager = userManager;
            _notificationManager = notificationManager;
            _notificationHandlers = notificationHandlers;
        }

        [Authorize(Policy = Policies.CanViewNotificationsForUser, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/users/me/notifications")]
        public async Task<IActionResult> GetNotifications(bool? seen = null, [FromQuery] int? skip = null, [FromQuery] int? take = null, [FromQuery] string[]? storeId = null)
        {
            var items = await _notificationManager.GetNotifications(new NotificationsQuery
            {
                Seen = seen,
                UserId = _userManager.GetUserId(User),
                Skip = skip,
                Take = take,
                StoreIds = storeId,
            });

            return Ok(items.Items.Select(ToModel));
        }

        [Authorize(Policy = Policies.CanViewNotificationsForUser, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/users/me/notifications/{id}")]
        public async Task<IActionResult> GetNotification(string id)
        {
            var items = await _notificationManager.GetNotifications(new NotificationsQuery
            {
                Ids = [id],
                UserId = _userManager.GetUserId(User)
            });

            return items.Count == 0 ? NotificationNotFound() : Ok(ToModel(items.Items.First()));
        }

        [Authorize(Policy = Policies.CanManageNotificationsForUser, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/users/me/notifications/{id}")]
        public async Task<IActionResult> UpdateNotification(string id, UpdateNotification request)
        {
            var items = await _notificationManager.ToggleSeen(
                new NotificationsQuery { Ids = [id], UserId = _userManager.GetUserId(User) }, request.Seen);

            return items.Count == 0 ? NotificationNotFound() : Ok(ToModel(items.First()));
        }

        [Authorize(Policy = Policies.CanManageNotificationsForUser, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/users/me/notifications/{id}")]
        public async Task<IActionResult> DeleteNotification(string id)
        {
            await _notificationManager.Remove(new NotificationsQuery
            {
                Ids = [id],
                UserId = _userManager.GetUserId(User)
            });

            return Ok();
        }

        [Authorize(Policy = Policies.CanManageNotificationsForUser, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/users/me/notification-settings")]
        public async Task<IActionResult> GetNotificationSettings()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
                return NotFound();
            var model = GetNotificationSettingsData(user);
            return Ok(model);
        }

        [Authorize(Policy = Policies.CanManageNotificationsForUser, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/users/me/notification-settings")]
        public async Task<IActionResult> UpdateNotificationSettings(UpdateNotificationSettingsRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
                return NotFound();
            if (request.Disabled.Contains("all"))
            {
                user.DisabledNotifications = "all";
            }
            else
            {
                var disabled = _notificationHandlers
                    .SelectMany(handler => handler.Meta.Select(tuple => tuple.identifier))
                    .Where(id => request.Disabled.Contains(id)).ToList();
                user.DisabledNotifications = disabled.Any() ? string.Join(';', disabled) + ";" : string.Empty;
            }
            await _userManager.UpdateAsync(user);

            var model = GetNotificationSettingsData(user);
            return Ok(model);
        }

        private static NotificationData ToModel(NotificationViewModel entity)
        {
            return new NotificationData
            {
                Id = entity.Id,
                Identifier = entity.Identifier,
                Type = entity.Type,
                CreatedTime = entity.Created,
                Body = entity.Body,
                StoreId = entity.StoreId,
                Seen = entity.Seen,
                Link = string.IsNullOrEmpty(entity.ActionLink) ? null : new Uri(entity.ActionLink)
            };
        }

        private IActionResult NotificationNotFound()
        {
            return this.CreateAPIError(404, "notification-not-found", "The notification was not found");
        }

        private NotificationSettingsData GetNotificationSettingsData(ApplicationUser user)
        {
            var disabledAll = user.DisabledNotifications == "all";
            var disabledNotifications = user.DisabledNotifications?.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList() ?? [];
            var notifications = _notificationHandlers.SelectMany(handler => handler.Meta.Select(tuple =>
                new NotificationSettingsItemData
                {
                    Identifier = tuple.identifier,
                    Name = tuple.name,
                    Enabled = !disabledAll && !disabledNotifications.Contains(tuple.identifier, StringComparer.InvariantCultureIgnoreCase)
                })).ToList();
            return new NotificationSettingsData { Notifications = notifications };
        }
    }
}
