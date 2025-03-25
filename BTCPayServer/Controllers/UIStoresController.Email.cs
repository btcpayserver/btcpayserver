using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    [HttpGet("{storeId}/email-settings")]
    public async Task<IActionResult> StoreEmailSettings(string storeId)
    {
        var store = HttpContext.GetStoreData();
        if (store == null)
            return NotFound();

        var settings = await GetCustomSettings(store.Id);

        return View(new EmailsViewModel(settings.Custom ?? new())
        {
            IsFallbackSetup = settings.Fallback is not null,
            IsCustomSMTP = settings.Custom is not null || settings.Fallback is null
        });
    }

    record AllEmailSettings(EmailSettings Custom, EmailSettings Fallback);
    private async Task<AllEmailSettings> GetCustomSettings(string storeId)
    {
        var sender = await _emailSenderFactory.GetEmailSender(storeId) as StoreEmailSender;
        if (sender is null)
            return new(null, null);
        var fallback = sender.FallbackSender is { } fb ? await fb.GetEmailSettings() : null;
        if (fallback?.IsComplete() is not true)
            fallback = null;
        return new(await sender.GetCustomSettings(), fallback);
    }

    [HttpPost("{storeId}/email-settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> StoreEmailSettings(string storeId, EmailsViewModel model, string command)
    {
        var store = HttpContext.GetStoreData();
        if (store == null)
            return NotFound();
        var settings = await GetCustomSettings(store.Id);
        model.IsFallbackSetup = settings.Fallback is not null;
        if (!model.IsFallbackSetup)
            model.IsCustomSMTP = true;
        if (model.IsCustomSMTP)
        {
            model.Settings.Validate("Settings.", ModelState);
            if (model.Settings.From is not null && !MailboxAddressValidator.IsMailboxAddress(model.Settings.From))
            {
                ModelState.AddModelError("Settings.From", StringLocalizer["Invalid email"]);
            }
            if (!ModelState.IsValid)
                return View(model);
        }

        var storeBlob = store.GetStoreBlob();
        var currentSettings = store.GetStoreBlob().EmailSettings;
        if (model is { IsCustomSMTP: true, Settings: { Password: null } })
            model.Settings.Password = currentSettings?.Password;

        if (command == "Test")
        {
            try
            {
                if (string.IsNullOrEmpty(model.TestEmail))
                    ModelState.AddModelError(nameof(model.TestEmail), new RequiredAttribute().FormatErrorMessage(nameof(model.TestEmail)));
                if (!ModelState.IsValid)
                    return View(model);
                var clientSettings = (model.IsCustomSMTP ? model.Settings : settings.Fallback) ?? new();
                using var client = await clientSettings.CreateSmtpClient();
                var message = clientSettings.CreateMailMessage(MailboxAddress.Parse(model.TestEmail), $"{store.StoreName}: Email test", StringLocalizer["You received it, the BTCPay Server SMTP settings work."], false);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Email sent to {0}. Please verify you received it.", model.TestEmail].Value;
            }
            catch (Exception ex)
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Error: {0}", ex.Message].Value;
            }
            return View(model);
        }
        else if (command == "ResetPassword")
        {
            if (storeBlob.EmailSettings is not null)
                storeBlob.EmailSettings.Password = null;
            store.SetStoreBlob(storeBlob);
            await _storeRepo.UpdateStore(store);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Email server password reset"].Value;
        }
        else if (!model.IsCustomSMTP && currentSettings is not null)
        {
            storeBlob.EmailSettings = null;
            store.SetStoreBlob(storeBlob);
            await _storeRepo.UpdateStore(store);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["You are now using server's email settings"].Value;
        }
        else if (model.IsCustomSMTP)
        {
            storeBlob.EmailSettings = model.Settings;
            store.SetStoreBlob(storeBlob);
            await _storeRepo.UpdateStore(store);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Email settings modified"].Value;
        }
        return RedirectToAction(nameof(StoreEmailSettings), new { storeId });
    }
}
