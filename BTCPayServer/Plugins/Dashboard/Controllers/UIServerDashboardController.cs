#nullable enable
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Dashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIServerDashboardController : Controller
{
    private const string ViewPath = "/Plugins/Dashboard/Views/ServerDashboard.cshtml";

    [HttpGet("server/dashboard")]
    public IActionResult ServerDashboard()
        => View(ViewPath, new Dashboard2ViewModel { OwnerKey = "server", IsSetUp = true });
}
