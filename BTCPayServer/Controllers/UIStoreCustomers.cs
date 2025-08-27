using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Abstractions.Constants;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewCustomers, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIStoreCustomers : Controller
{
    [HttpGet("stores/{storeId}/customers")]
    public IActionResult Index()
    {
        return View();
    }
}
