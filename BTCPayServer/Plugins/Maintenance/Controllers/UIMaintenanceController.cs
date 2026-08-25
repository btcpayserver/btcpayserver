using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.Maintenance.Models;
using BTCPayServer.Services;
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
    CheckHostCommandsHostedService hostCommandState,
    ProcessRunner processRunner,
    IHostApplicationLifetime applicationLifetime,
    Logs logs,
    IStringLocalizer stringLocalizer) : Controller
{
    private static readonly TimeSpan LongOperation = TimeSpan.FromMinutes(20);

    [HttpGet("server/maintenance")]
    public IActionResult Maintenance()
    {
        if (!hostCommandState.BTCPayHostAvailable)
            return NotFound();

        var vm = new MaintenanceViewModel
        {
            SupportedCommands = hostCommandState.SupportedCommands,
            DNSDomain = Request.Host.Host
        };

        if (IPAddress.TryParse(vm.DNSDomain, out var unused))
            vm.DNSDomain = null;

        return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);
    }

    [HttpPost("server/maintenance")]
    public async Task<IActionResult> Maintenance(MaintenanceViewModel vm, string command)
    {
        vm.SupportedCommands = hostCommandState.SupportedCommands;
        if (command != "soft-restart" && !hostCommandState.BTCPayHostAvailable)
        {
            TempData[WellKnownTempData.ErrorMessage] = stringLocalizer["Maintenance feature requires local BTCPay commands."].Value;
            return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);
        }
        if (!ModelState.IsValid)
            return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);

        if (command == "changedomain" && vm.SupportedCommands.Contains(HostCommands.ChangeDomain))
        {
            if (string.IsNullOrWhiteSpace(vm.DNSDomain))
            {
                ModelState.AddModelError(nameof(vm.DNSDomain), $"Required field");
                return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);
            }
            vm.DNSDomain = vm.DNSDomain.Trim().ToLowerInvariant();
            if (vm.DNSDomain.Equals(Request.Host.Host, StringComparison.OrdinalIgnoreCase))
                return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);
            if (IPAddress.TryParse(vm.DNSDomain, out var unused))
            {
                ModelState.AddModelError(nameof(vm.DNSDomain), $"This should be a domain name");
                return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);
            }
            if (vm.DNSDomain.Equals(Request.Host.Host, StringComparison.InvariantCultureIgnoreCase))
            {
                ModelState.AddModelError(nameof(vm.DNSDomain), $"The server is already set to use this domain");
                return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);
            }
            if (Uri.CheckHostName(vm.DNSDomain) != UriHostNameType.Dns)
            {
                ModelState.AddModelError(nameof(vm.DNSDomain), $"Invalid hostname");
                return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);
            }
            var builder = new UriBuilder();
            try
            {
                builder.Scheme = Request.Scheme;
                builder.Host = vm.DNSDomain;
                var addresses1 = GetAddressAsync(Request.Host.Host);
                var addresses2 = GetAddressAsync(vm.DNSDomain);
                await Task.WhenAll(addresses1, addresses2);

                var addressesSet = addresses1.GetAwaiter().GetResult().Select(c => c.ToString()).ToHashSet();
                var hasCommonAddress = addresses2.GetAwaiter().GetResult().Select(c => c.ToString()).Any(s => addressesSet.Contains(s));
                if (!hasCommonAddress)
                {
                    ModelState.AddModelError(nameof(vm.DNSDomain), $"Invalid host ({vm.DNSDomain} is not pointing to this BTCPay instance)");
                    return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);
                }
            }
            catch (Exception ex)
            {
                var messages = new List<object>();
                messages.Add(ex.Message);
                if (ex.InnerException != null)
                    messages.Add(ex.InnerException.Message);
                ModelState.AddModelError(nameof(vm.DNSDomain), $"Invalid domain ({string.Join(", ", messages.ToArray())})");
                return View("/Plugins/Maintenance/Views/Maintenance.cshtml", vm);
            }

            _ = processRunner.RunHostCommand(HostCommands.ChangeDomain, new[] { vm.DNSDomain }, TimeSpan.FromMinutes(20));
            builder.Path = null;
            builder.Query = null;
            TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["Domain name changing... the server will restart, please use \"{0}\" (this page won't reload automatically)", builder.Uri.AbsoluteUri].Value;
        }
        else if (command == "update" && vm.SupportedCommands.Contains(HostCommands.Update))
        {
            _ = processRunner.RunHostCommand(HostCommands.Update, null, LongOperation);
            TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["The server might restart soon if an update is available... (this page won't reload automatically)"].Value;
        }
        else if (command == "clean" && vm.SupportedCommands.Contains(HostCommands.Clean))
        {
            _ = processRunner.RunHostCommand(HostCommands.Clean, null, LongOperation);
            TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["The old docker images will be cleaned soon..."].Value;
        }
        else if (command == "restart" && vm.SupportedCommands.Contains(HostCommands.Restart))
        {
            _ = processRunner.RunHostCommand(HostCommands.Restart, null, LongOperation);
            logs.PayServer.LogInformation("A hard restart has been requested");
            TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["BTCPay will restart momentarily."].Value;
        }
        else if (command == "soft-restart")
        {
            TempData[WellKnownTempData.SuccessMessage] = stringLocalizer["BTCPay will restart momentarily."].Value;
            logs.PayServer.LogInformation("A soft restart has been requested");
            _ = Task.Delay(3000).ContinueWith((t) => applicationLifetime.StopApplication());
        }
        else
        {
            return NotFound();
        }
        return RedirectToAction(nameof(Maintenance));
    }

    private Task<IPAddress[]> GetAddressAsync(string domainOrIP)
    {
        return IPAddress.TryParse(domainOrIP, out var ip) ? Task.FromResult(new[] { ip }) : Dns.GetHostAddressesAsync(domainOrIP);
    }
}
