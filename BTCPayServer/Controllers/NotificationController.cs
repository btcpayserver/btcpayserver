using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Events.Notifications;
using BTCPayServer.Filters;
using BTCPayServer.Models.NotificationViewModels;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [BitpayAPIConstraint(false)]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class NotificationController : Controller
    {
        private readonly EventAggregator _eventAggregator;

        public NotificationController(EventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int skip = 0, int count = 50, int timezoneOffset = 0)
        {
            var model = new IndexViewModel()
            {
                Items = new List<IndexViewModel.NoticeDataHolder>
                {
                    new IndexViewModel.NoticeDataHolder
                    {
                        Id = 1,
                        Body = "Hello world",
                        Level = "Admin",
                        Created = new DateTime(2020, 01, 05)
                    },
                    new IndexViewModel.NoticeDataHolder
                    {
                        Id = 1,
                        Body = "Notification number 2",
                        Level = "Store",
                        Created = new DateTime(2020, 02, 07)
                    },
                    new IndexViewModel.NoticeDataHolder
                    {
                        Id = 1,
                        Body = "Up unpacked friendly ecstatic so possible humoured do. Ample end might folly quiet one set spoke her. We no am former valley assure. Four need spot ye said we find mile.",
                        Level = "User",
                        Created = new DateTime(2020, 03, 01)
                    },
                    new IndexViewModel.NoticeDataHolder
                    {
                        Id = 1,
                        Body = "New version of BTCPayServer is detected.",
                        Level = "Admin",
                        Created = new DateTime(2020, 06, 22)
                    },
                    new IndexViewModel.NoticeDataHolder
                    {
                        Id = 1,
                        Body = "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog.",
                        Level = "Store",
                        Created = new DateTime(2020, 04, 17)
                    },
                    new IndexViewModel.NoticeDataHolder
                    {
                        Id = 1,
                        Body = "New invoice paid",
                        Level = "User",
                        Created = new DateTime(2020, 05, 07)
                    }
                }
            };
            model.Items = model.Items.OrderByDescending(a => a.Created).ToList();
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Generate()
        {
            _eventAggregator.NoticeNewVersion("1.1.1");
            return RedirectToAction(nameof(Index));
        }
    }
}
