using BTCPayServer.Plugins.BoltcardFactory;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.BoltcardBalance.Controllers
{
    [AutoValidateAntiforgeryToken]
    public class UIBoltcardBalanceController : Controller
    {
        [HttpGet("boltcards/balance")]
        public IActionResult ScanCard()
        {
            return base.View($"{BoltcardBalancePlugin.ViewsDirectory}/ScanCard.cshtml");
        }
    }
}
