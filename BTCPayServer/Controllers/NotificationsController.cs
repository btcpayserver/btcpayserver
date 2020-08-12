using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models.NotificationViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [BitpayAPIConstraint(false)]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Route("[controller]/[action]")]
    public class NotificationsController : Controller
    {
        private readonly BTCPayServerEnvironment _env;
        private readonly ApplicationDbContext _db;
        private readonly NotificationSender _notificationSender;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationManager _notificationManager;
        private readonly EventAggregator _eventAggregator;

        public NotificationsController(BTCPayServerEnvironment env,
            ApplicationDbContext db,
            NotificationSender notificationSender,
            UserManager<ApplicationUser> userManager,
            NotificationManager notificationManager,
            EventAggregator eventAggregator)
        {
            _env = env;
            _db = db;
            _notificationSender = notificationSender;
            _userManager = userManager;
            _notificationManager = notificationManager;
            _eventAggregator = eventAggregator;
        }

        [HttpGet]
        public IActionResult GetNotificationDropdownUI()
        {
            return ViewComponent("NotificationsDropdown");
        }


        [HttpGet]
        public async Task<IActionResult> SubscribeUpdates(CancellationToken cancellationToken)
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                return BadRequest();
            }
            var websocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var userId = _userManager.GetUserId(User);
            var websocketHelper = new WebSocketHelper(websocket);
            IEventAggregatorSubscription subscription = null;
            try
            {
                subscription = _eventAggregator.Subscribe<UserNotificationsUpdatedEvent>(async evt =>
                {
                    if (evt.UserId == userId)
                    {
                        await websocketHelper.Send("update");
                    }
                });

                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(2000, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                // ignored
            }
            finally
            {
                subscription?.Dispose();
                await websocketHelper.DisposeAsync(CancellationToken.None);
            }

            return new EmptyResult();
        }

        [HttpGet]
        public IActionResult Index(int skip = 0, int count = 50, int timezoneOffset = 0)
        {
            if (!ValidUserClaim(out var userId))
                return RedirectToAction("Index", "Home");

            var model = new IndexViewModel()
            {
                Skip = skip,
                Count = count,
                Items = _db.Notifications
                    .OrderByDescending(a => a.Created)
                    .Skip(skip).Take(count)
                    .Where(a => a.ApplicationUserId == userId)
                    .Select(a => _notificationManager.ToViewModel(a))
                    .ToList(),
                Total = _db.Notifications.Count(a => a.ApplicationUserId == userId)
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Generate(string version)
        {
            if (_env.NetworkType != NBitcoin.NetworkType.Regtest)
                return NotFound();
            await _notificationSender.SendNotification(new AdminScope(), new NewVersionNotification(version));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> FlipRead(string id)
        {
            if (ValidUserClaim(out var userId))
            {
                var notif = _db.Notifications.Single(a => a.Id == id && a.ApplicationUserId == userId);
                notif.Seen = !notif.Seen;
                await _db.SaveChangesAsync();
                _notificationManager.InvalidateNotificationCache(userId);
                return RedirectToAction(nameof(Index));
            }

            return BadRequest();
        }

        [HttpGet]
        public async Task<IActionResult> NotificationPassThrough(string id)
        {
            if (ValidUserClaim(out var userId))
            {
                var notif = _db.Notifications.Single(a => a.Id == id && a.ApplicationUserId == userId);
                if (!notif.Seen)
                {
                    notif.Seen = !notif.Seen;
                    await _db.SaveChangesAsync();
                    _notificationManager.InvalidateNotificationCache(userId);
                }

                var vm = _notificationManager.ToViewModel(notif);
                if (string.IsNullOrEmpty(vm.ActionLink))
                {
                    return RedirectToAction(nameof(Index));
                }

                return Redirect(vm.ActionLink);
            }

            return NotFound();
        }


        [HttpPost]
        public async Task<IActionResult> MassAction(string command, string[] selectedItems)
        {
            if (!ValidUserClaim(out var userId))
            {
                return NotFound();
            }

            if (command.StartsWith("flip-individual", StringComparison.InvariantCulture))
            {
                var id = command.Split(":")[1];
                return await FlipRead(id);
            }

            if (selectedItems != null)
            {
                var items = _db.Notifications.Where(a => a.ApplicationUserId == userId && selectedItems.Contains(a.Id));
                switch (command)
                {
                    case "delete":
                        _db.Notifications.RemoveRange(items);

                        break;
                    case "mark-seen":
                        foreach (NotificationData notificationData in items)
                        {
                            notificationData.Seen = true;
                        }

                        break;
                    case "mark-unseen":
                        foreach (NotificationData notificationData in items)
                        {
                            notificationData.Seen = false;
                        }

                        break;
                }

                await _db.SaveChangesAsync();
                _notificationManager.InvalidateNotificationCache(userId);
                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(nameof(Index));
        }

        private bool ValidUserClaim(out string userId)
        {
            userId = _userManager.GetUserId(User);
            return userId != null;
        }
    }
}
