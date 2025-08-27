using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using  BTCPayServer.Abstractions.Constants;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewSubscriptions, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIStoreSubscriptions : Controller
{
    [HttpGet("stores/{storeId}/subscriptions")]
    public IActionResult Index()
    {
        return View();
    }
}
