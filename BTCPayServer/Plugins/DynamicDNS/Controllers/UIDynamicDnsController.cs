#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.DynamicDNS.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;

namespace BTCPayServer.Plugins.DynamicDNS.Controllers;

[Area(DynamicDnsPlugin.Area)]
[Authorize(Policy = Client.Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIDynamicDnsController(SettingsRepository settingsRepository, IHttpClientFactory httpClientFactory, IStringLocalizer stringLocalizer)
    : Controller
{
    private IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    [HttpGet("~/server/services/dynamic-dns")]
    public async Task<IActionResult> DynamicDnsServices()
    {
        var settings = await settingsRepository.GetSettingAsync<DynamicDnsSettings>() ?? new DynamicDnsSettings();
        return View(settings.Services.Select(s => new DynamicDnsViewModel
        {
            Settings = s
        }).ToArray());
    }

    [HttpGet("~/server/services/dynamic-dns/{hostname}")]
    public async Task<IActionResult> DynamicDnsServices(string hostname)
    {
        var settings = await settingsRepository.GetSettingAsync<DynamicDnsSettings>() ?? new DynamicDnsSettings();
        var service = settings.Services.FirstOrDefault(s => s.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
        if (service == null)
            return NotFound();
        return View(nameof(DynamicDnsService), new DynamicDnsViewModel
        {
            Modify = true,
            Settings = service
        });
    }

    [HttpPost("~/server/services/dynamic-dns")]
    public async Task<IActionResult> DynamicDnsService(DynamicDnsViewModel viewModel, string? command = null)
    {
        if (!ModelState.IsValid)
            return View(viewModel);

        if (command != "Save")
            return View(new DynamicDnsViewModel { Settings = new DynamicDnsService() });

        var settings = await settingsRepository.GetSettingAsync<DynamicDnsSettings>() ?? new DynamicDnsSettings();
        viewModel.Settings.Hostname = viewModel.Settings.Hostname?.Trim().ToLowerInvariant();
        var index = settings.Services.FindIndex(d => d.Hostname.Equals(viewModel.Settings.Hostname, StringComparison.OrdinalIgnoreCase));
        if (index != -1)
        {
            ModelState.AddModelError(nameof(viewModel.Settings.Hostname), "This hostname already exists");
            return View(viewModel);
        }
        var errorMessage = await viewModel.Settings.SendUpdateRequest(httpClientFactory.CreateClient());
        if (errorMessage != null)
        {
            ModelState.AddModelError(string.Empty, errorMessage);
            return View(viewModel);
        }

        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The Dynamic DNS has been successfully queried, your configuration is saved"].Value;
        viewModel.Settings.LastUpdated = DateTimeOffset.UtcNow;
        settings.Services.Add(viewModel.Settings);
        await settingsRepository.UpdateSetting(settings);
        return RedirectToAction(nameof(DynamicDnsServices));
    }

    [HttpPost("~/server/services/dynamic-dns/{hostname}")]
    public async Task<IActionResult> DynamicDnsService(DynamicDnsViewModel viewModel, string hostname, string? command = null)
    {
        if (!ModelState.IsValid)
            return View(viewModel);

        var settings = await settingsRepository.GetSettingAsync<DynamicDnsSettings>() ?? new DynamicDnsSettings();
        var index = settings.Services.FindIndex(d => d.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
        if (index == -1)
            return NotFound();

        if (viewModel.Settings.Password == null)
            viewModel.Settings.Password = settings.Services[index].Password;
        viewModel.Settings.Hostname = settings.Services[index].Hostname;
        if (!viewModel.Settings.Enabled)
        {
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The Dynamic DNS service has been disabled"].Value;
            viewModel.Settings.LastUpdated = null;
        }
        else
        {
            var errorMessage = await viewModel.Settings.SendUpdateRequest(httpClientFactory.CreateClient());
            if (errorMessage != null)
            {
                ModelState.AddModelError(string.Empty, errorMessage);
                return View(viewModel);
            }

            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The Dynamic DNS has been successfully queried, your configuration is saved"].Value;
            viewModel.Settings.LastUpdated = DateTimeOffset.UtcNow;
        }

        viewModel.Settings.Hostname = hostname;
        settings.Services[index] = viewModel.Settings;
        await settingsRepository.UpdateSetting(settings);
        RouteData.Values.Remove(nameof(hostname));
        return RedirectToAction(nameof(DynamicDnsServices));
    }

    [HttpGet("~/server/services/dynamic-dns/{hostname}/delete")]
    public async Task<IActionResult> DeleteDynamicDnsService(string hostname)
    {
        var settings = await settingsRepository.GetSettingAsync<DynamicDnsSettings>() ?? new DynamicDnsSettings();
        var index = settings.Services.FindIndex(d => d.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
        if (index == -1)
            return NotFound();
        return View("Confirm",
            new ConfirmModel("Delete dynamic DNS service",
                $"Deleting the dynamic DNS service for <strong>{WebUtility.HtmlEncode(hostname)}</strong> means your BTCPay Server will stop updating the associated DNS record periodically.", StringLocalizer["Delete"]));
    }

    [HttpPost("~/server/services/dynamic-dns/{hostname}/delete")]
    public async Task<IActionResult> DeleteDynamicDnsServicePost(string hostname)
    {
        var settings = await settingsRepository.GetSettingAsync<DynamicDnsSettings>() ?? new DynamicDnsSettings();
        var index = settings.Services.FindIndex(d => d.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
        if (index == -1)
            return NotFound();
        settings.Services.RemoveAt(index);
        await settingsRepository.UpdateSetting(settings);
        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Dynamic DNS service successfully removed"].Value;
        RouteData.Values.Remove(nameof(hostname));
        return RedirectToAction(nameof(DynamicDnsServices));
    }
}
