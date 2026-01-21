using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.PullPayments.Boltcards;

[Area(PullPaymentsBoltcardsPlugin.Area)]
public class UIBoltcardPullPaymentSetupController(IStringLocalizer stringLocalizer) : Controller
{
    public IStringLocalizer StringLocalizer { get; set; } = stringLocalizer;
    [AllowAnonymous]
    [HttpGet("pull-payments/{pullPaymentId}/boltcard/{command}")]
    public IActionResult SetupBoltcard(string pullPaymentId, string command)
    {
        return View(nameof(SetupBoltcard),
            new SetupBoltcardViewModel
            {
                ReturnUrl = Url.Action(nameof(UIPullPaymentController.ViewPullPayment), "UIPullPayment", new { pullPaymentId }),
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
        return RedirectToAction(nameof(UIPullPaymentController.ViewPullPayment), "UIPullPayment", new { pullPaymentId });
    }
}
