#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Emails.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using MimeKit;

namespace BTCPayServer.Plugins.Emails.Controllers;

public abstract class UIEmailControllerBase(IStringLocalizer stringLocalizer) : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    protected class Context
    {
        public Func<EmailSettings, EmailsViewModel> CreateEmailViewModel { get; set; } = null!;
        public string StoreId { get; set; } = null!;
    }

    protected abstract Task<EmailSettings> GetEmailSettings(Context ctx);

    protected virtual Task<EmailSettings> GetEmailSettingsForTest(Context ctx, EmailsViewModel viewModel)
        => Task.FromResult(viewModel.Settings);

    protected abstract Task SaveEmailSettings(Context ctx, EmailSettings settings, EmailsViewModel? viewModel = null);
    protected abstract IActionResult RedirectToEmailSettings(Context ctx);
    protected abstract Task<(string Subject, string Body)> GetTestMessage(Context ctx);

    protected async Task<IActionResult> EmailSettingsCore(Context ctx)
    {
        var email = await GetEmailSettings(ctx);
        var vm = ctx.CreateEmailViewModel(email);
        return View("EmailSettings", vm);
    }
    protected async Task<IActionResult> EmailSettingsCore(Context ctx, EmailsViewModel model, string command)
    {
        if (command == "Test")
        {
            try
            {
                if (model.PasswordSet)
                {
                    var settings = await GetEmailSettings(ctx);
                    model.Settings.Password = settings.Password;
                }

                model.Settings.Validate("Settings.", ModelState);
                if (string.IsNullOrEmpty(model.TestEmail))
                    ModelState.AddModelError(nameof(model.TestEmail), new RequiredAttribute().FormatErrorMessage(nameof(model.TestEmail)));
                if (!ModelState.IsValid)
                    return await EmailSettingsCore(ctx);
                var mess = await GetTestMessage(ctx);
                var settingsForTest = await GetEmailSettingsForTest(ctx, model);
                if (!settingsForTest.IsComplete())
                {
                    ModelState.AddModelError(nameof(model.TestEmail), "Email settings are not complete.");
                    return await EmailSettingsCore(ctx);
                }
                using (var client = await settingsForTest.CreateSmtpClient())
                using (var message = settingsForTest.CreateMailMessage(MailboxAddress.Parse(model.TestEmail), mess.Subject,
                           mess.Body, false))
                {
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                }

                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Email sent to {0}. Please verify you received it.", model.TestEmail].Value;
            }
            catch (Exception ex)
            {
                TempData[WellKnownTempData.ErrorMessage] = ex.Message;
            }

            return await EmailSettingsCore(ctx);
        }
        else if (command == "ResetPassword")
        {
            var settings = await GetEmailSettings(ctx);
            settings.Password = null;
            await SaveEmailSettings(ctx, settings);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Email server password reset"].Value;
        }
        else if (command == "mailpit")
        {
            model.Settings.Server = "localhost";
            model.Settings.Port = 34219;
            model.Settings.EnabledCertificateCheck = false;
            model.Settings.Login ??= "store@example.com";
            model.Settings.From ??= "store@example.com";
            model.Settings.Password ??= "password";
            await SaveEmailSettings(ctx, model.Settings);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Info,
                AllowDismiss = true,
                Html =
                    "Mailpit is now running on <a href=\"http://localhost:34218\" target=\"_blank\" class=\"alert-link\">localhost</a>. You can use it to test your SMTP settings."
            });
        }
        else
        {
            // save if user provided valid email; this will also clear settings if no model.Settings.From
            if (model.Settings.From is not null && !MailboxAddressValidator.IsMailboxAddress(model.Settings.From))
            {
                ModelState.AddModelError("Settings.From", StringLocalizer["Invalid email"]);
                return await EmailSettingsCore(ctx);
            }

            var oldSettings = await GetEmailSettings(ctx);
            if (!string.IsNullOrEmpty(oldSettings.Password))
                model.Settings.Password = oldSettings.Password;

            await SaveEmailSettings(ctx, model.Settings, model);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Email settings saved"].Value;
        }

        return RedirectToEmailSettings(ctx);
    }
}
