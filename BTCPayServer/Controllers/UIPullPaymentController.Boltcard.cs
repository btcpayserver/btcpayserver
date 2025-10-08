using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class UIPullPaymentController
    {
        [AllowAnonymous]
        [HttpGet("pull-payments/{pullPaymentId}/boltcard/{command}")]
        public IActionResult SetupBoltcard(string pullPaymentId, string command)
        {
            return View(nameof(SetupBoltcard),
                new SetupBoltcardViewModel
                {
                    ReturnUrl = Url.Action(nameof(ViewPullPayment), "UIPullPayment", new { pullPaymentId }),
                    BoltcardUrl = Url.ActionAbsolute(this.Request, nameof(UIBoltcardController.GetWithdrawRequest), "UIBoltcard").AbsoluteUri,
                    NewCard = command == "configure-boltcard",
                    PullPaymentId = pullPaymentId
                });
        }

        [AllowAnonymous]
        [HttpPost("pull-payments/{pullPaymentId}/boltcard/{command}")]
        public IActionResult SetupBoltcardPost(string pullPaymentId, string command)
        {
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Boltcard is configured"].Value;
            return RedirectToAction(nameof(ViewPullPayment), new { pullPaymentId });
        }
    }
}
