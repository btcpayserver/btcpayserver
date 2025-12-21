using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers;

public partial class UIManageController
{
    [HttpGet]
    public ActionResult LoginCodes()
    {
        return View();
    }
}
