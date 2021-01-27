using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
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
        private readonly NotificationSender _notificationSender;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationManager _notificationManager;
        private readonly EventAggregator _eventAggregator;

        public NotificationsController(BTCPayServerEnvironment env,
            NotificationSender notificationSender,
            UserManager<ApplicationUser> userManager,
            NotificationManager notificationManager,
            EventAggregator eventAggregator)
        {
            _env = env;
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
#if DEBUG
        [HttpGet]
        public async Task<IActionResult> GenerateJunk(int x = 100, bool admin = true)
        {
            for (int i = 0; i < x; i++)
            {
                await _notificationSender.SendNotification(
                    admin ? (NotificationScope)new AdminScope() : new UserScope(_userManager.GetUserId(User)),
                    new JunkNotification());
            }

            return RedirectToAction("Index");
        }
#endif
        [HttpGet]
        public async Task<IActionResult> Index(int skip = 0, int count = 50, int timezoneOffset = 0)
        {
            if (!ValidUserClaim(out var userId))
                return RedirectToAction("Index", "Home");

            var res = await _notificationManager.GetNotifications(new NotificationsQuery()
            {
                Skip = skip, Take = count, UserId = userId
            });

            var model = new IndexViewModel() {Skip = skip, Count = count, Items = res.Items, Total = res.Count};

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Generate(string version)
        {
            if (_env.NetworkType != NBitcoin.ChainName.Regtest)
                return NotFound();
            await _notificationSender.SendNotification(new AdminScope(), new NewVersionNotification(version));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> FlipRead(string id)
        {
            if (ValidUserClaim(out var userId))
            {
                await _notificationManager.ToggleSeen(new NotificationsQuery() {Ids = new[] {id}, UserId = userId}, null);
                return RedirectToAction(nameof(Index));
            }

            return BadRequest();
        }

        [HttpGet]
        public async Task<IActionResult> NotificationPassThrough(string id)
        {
            if (ValidUserClaim(out var userId))
            {
                var items = await
                    _notificationManager.ToggleSeen(new NotificationsQuery()
                    {
                        Ids = new[] {id}, UserId = userId
                    }, true);
                
                var link = items.FirstOrDefault()?.ActionLink ?? "";
                if (string.IsNullOrEmpty(link))
                {
                    return RedirectToAction(nameof(Index));
                }

                return Redirect(link);
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
                switch (command)
                {
                    case "delete":
                        await _notificationManager.Remove(new NotificationsQuery()
                        {
                            UserId = userId, Ids = selectedItems
                        });

                        break;
                    case "mark-seen":
                        await _notificationManager.ToggleSeen(new NotificationsQuery()
                        {
                            UserId = userId, Ids = selectedItems, Seen = false
                        }, true);

                        break;
                    case "mark-unseen":
                        await _notificationManager.ToggleSeen(new NotificationsQuery()
                        {
                            UserId = userId, Ids = selectedItems, Seen = true
                        }, false);
                        break;
                }
                return RedirectToAction(nameof(Index));
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> MarkAllAsSeen(string returnUrl)
        {
            if (!ValidUserClaim(out var userId))
            {
                return NotFound();
            }
            await _notificationManager.ToggleSeen(new NotificationsQuery() {Seen = false, UserId = userId}, true);
            return Redirect(returnUrl);
        }

        private bool ValidUserClaim(out string userId)
        {
            userId = _userManager.GetUserId(User);
            return userId != null;
        }
    }
}
