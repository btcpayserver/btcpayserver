using System;
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

        public GreenfieldNotificationsController(UserManager<ApplicationUser> userManager,
            NotificationManager notificationManager)
        {
            _userManager = userManager;
            _notificationManager = notificationManager;
        }

        [Authorize(Policy = Policies.CanViewNotificationsForUser,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/users/me/notifications")]
        public async Task<IActionResult> GetNotifications(bool? seen = null, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        {
            var items = await _notificationManager.GetNotifications(new NotificationsQuery()
            {
                Seen = seen,
                UserId = _userManager.GetUserId(User),
                Skip = skip,
                Take = take
            });

            return Ok(items.Items.Select(ToModel));
        }

        [Authorize(Policy = Policies.CanViewNotificationsForUser,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/users/me/notifications/{id}")]
        public async Task<IActionResult> GetNotification(string id)
        {
            var items = await _notificationManager.GetNotifications(new NotificationsQuery()
            {
                Ids = new[] { id },
                UserId = _userManager.GetUserId(User)
            });

            if (items.Count == 0)
            {
                return NotificationNotFound();
            }

            return Ok(ToModel(items.Items.First()));
        }

        [Authorize(Policy = Policies.CanManageNotificationsForUser,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/users/me/notifications/{id}")]
        public async Task<IActionResult> UpdateNotification(string id, UpdateNotification request)
        {
            var items = await _notificationManager.ToggleSeen(
                new NotificationsQuery() { Ids = new[] { id }, UserId = _userManager.GetUserId(User) }, request.Seen);

            if (items.Count == 0)
            {
                return NotificationNotFound();
            }

            return Ok(ToModel(items.First()));
        }

        [Authorize(Policy = Policies.CanManageNotificationsForUser,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/users/me/notifications/{id}")]
        public async Task<IActionResult> DeleteNotification(string id)
        {
            await _notificationManager.Remove(new NotificationsQuery()
            {
                Ids = new[] { id },
                UserId = _userManager.GetUserId(User)
            });

            return Ok();
        }

        private NotificationData ToModel(NotificationViewModel entity)
        {
            return new NotificationData
            {
                Id = entity.Id,
                Identifier = entity.Identifier,
                Type = entity.Type,
                CreatedTime = entity.Created,
                Body = entity.Body,
                Seen = entity.Seen,
                Link = string.IsNullOrEmpty(entity.ActionLink) ? null : new Uri(entity.ActionLink)
            };
        }
        private IActionResult NotificationNotFound()
        {
            return this.CreateAPIError(404, "notification-not-found", "The notification was not found");
        }
    }
}
