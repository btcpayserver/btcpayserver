using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.Maintenance.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;

namespace BTCPayServer.Plugins.Maintenance.Controllers;

[Authorize(Policy = Policies.CanModifyServerSettings,
    AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Area(MaintenancePlugin.Area)]
public class UIMaintenanceController(
    HostIntegrationState hostIntegrationState,
    IHostApplicationLifetime applicationLifetime,
    Logs logs,
    IStringLocalizer stringLocalizer) : Controller
{
    public IStringLocalizer StringLocalizer = stringLocalizer;

    [HttpGet("server/maintenance")]
    public IActionResult Maintenance()
    {
        var hostIntegration = hostIntegrationState.Current;
        if (hostIntegration is null)
            return NotFound();

        var vm = new MaintenanceViewModel
        {
            SupportedCommands = (hostIntegration.Commands ?? []).ToHashSet(),
            DNSDomain = Request.Host.Host
        };

        if (IPAddress.TryParse(vm.DNSDomain, out var unused))
            vm.DNSDomain = null;

        return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);
    }

    [HttpPost("server/maintenance")]
    public IActionResult Maintenance(string command)
    {
        if (command != "soft-restart")
            return NotFound();

        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["BTCPay will restart momentarily."].Value;
        logs.PayServer.LogInformation("A soft restart has been requested");
        _ = Task.Delay(3000).ContinueWith((t) => applicationLifetime.StopApplication());
        return RedirectToAction(nameof(Maintenance));
    }
}
