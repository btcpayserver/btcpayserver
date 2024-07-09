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

namespace BTCPayServer.Controllers
{
    public partial class UIStoresController
    {
        [HttpGet("{storeId}/emails")]
        public async Task<IActionResult> StoreEmails(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var blob = store.GetStoreBlob();
            if (blob.EmailSettings?.IsComplete() is not true && !TempData.HasStatusMessage())
            {
                var emailSender = await _emailSenderFactory.GetEmailSender(store.Id) as StoreEmailSender;
                if (!await IsSetupComplete(emailSender?.FallbackSender))
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Warning,
                        Html = $"You need to configure email settings before this feature works. <a class='alert-link' href='{Url.Action("StoreEmailSettings", new { storeId })}'>Configure store email settings</a>."
                    });
                }
            }

            var vm = new StoreEmailRuleViewModel { Rules = blob.EmailRules ?? [] };
            return View(vm);
        }

        [HttpPost("{storeId}/emails")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> StoreEmails(string storeId, StoreEmailRuleViewModel vm, string command)
        {
            vm.Rules ??= new List<StoreEmailRule>();
            int commandIndex = 0;
            
            var indSep = command.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (indSep.Length > 1)
            {
                commandIndex = int.Parse(indSep[1], CultureInfo.InvariantCulture);
            }

            if (command.StartsWith("remove", StringComparison.InvariantCultureIgnoreCase))
            {
                vm.Rules.RemoveAt(commandIndex);
            }
            else if (command == "add")
            {
                vm.Rules.Add(new StoreEmailRule());

                return View(vm);
            }

            for (var i = 0; i < vm.Rules.Count; i++)
            {
                var rule = vm.Rules[i];

                if (!string.IsNullOrEmpty(rule.To) && (rule.To.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Any(s => !MailboxAddressValidator.TryParse(s, out _))))
                {
                    ModelState.AddModelError($"{nameof(vm.Rules)}[{i}].{nameof(rule.To)}",
                        "Invalid mailbox address provided. Valid formats are: 'test@example.com' or 'Firstname Lastname <test@example.com>'");
                }
                else if (!rule.CustomerEmail && string.IsNullOrEmpty(rule.To))
                    ModelState.AddModelError($"{nameof(vm.Rules)}[{i}].{nameof(rule.To)}",
                        "Either recipient or \"Send the email to the buyer\" is required");
            }

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var store = HttpContext.GetStoreData();

            if (store == null)
                return NotFound();

            string message = "";

            // update rules
            var blob = store.GetStoreBlob();
            blob.EmailRules = vm.Rules;
            if (store.SetStoreBlob(blob))
            {
                await _Repo.UpdateStore(store);
                message += "Store email rules saved. ";
            }

            if (command.StartsWith("test", StringComparison.InvariantCultureIgnoreCase))
            {
                try
                {
                    var rule = vm.Rules[commandIndex];
                    var emailSender = await _emailSenderFactory.GetEmailSender(store.Id);
                    if (await IsSetupComplete(emailSender))
                    {
                        var recipients = rule.To.Split(",", StringSplitOptions.RemoveEmptyEntries)
                            .Select(o =>
                            {
                                MailboxAddressValidator.TryParse(o, out var mb);
                                return mb;
                            })
                            .Where(o => o != null)
                            .ToArray();
                        
                        emailSender.SendEmail(recipients.ToArray(), null, null, $"[TEST] {rule.Subject}", rule.Body);
                        message += "Test email sent — please verify you received it.";
                    }
                    else
                    {
                        message += "Complete the email setup to send test emails.";
                    }
                }
                catch (Exception ex)
                {
                    TempData[WellKnownTempData.ErrorMessage] = message + "Error sending test email: " + ex.Message;
                    return RedirectToAction("StoreEmails", new { storeId });
                }
            }

            if (!string.IsNullOrEmpty(message))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Message = message
                });
            }

            return RedirectToAction("StoreEmails", new { storeId });
        }

        public class StoreEmailRuleViewModel
        {
            public List<StoreEmailRule> Rules { get; set; }
        }

        public class StoreEmailRule
        {
            [Required]
            public string Trigger { get; set; }
            
            public bool CustomerEmail { get; set; }
            
           
            public string To { get; set; }
            
            [Required]
            public string Subject { get; set; }
            
            [Required]
            public string Body { get; set; }
        }

        [HttpGet("{storeId}/email-settings")]
        public async Task<IActionResult> StoreEmailSettings(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var blob = store.GetStoreBlob();
            var data = blob.EmailSettings ?? new EmailSettings();
            var fallbackSettings = await _emailSenderFactory.GetEmailSender(store.Id) is StoreEmailSender { FallbackSender: not null } storeSender
                ? await storeSender.FallbackSender.GetEmailSettings()
                : null;
            var vm = new EmailsViewModel(data, fallbackSettings);
            
            return View(vm);
        }

        [HttpPost("{storeId}/email-settings")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> StoreEmailSettings(string storeId, EmailsViewModel model, string command, [FromForm] bool useCustomSMTP = false)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            model.FallbackSettings = await _emailSenderFactory.GetEmailSender(store.Id) is StoreEmailSender { FallbackSender: not null } storeSender
                ? await storeSender.FallbackSender.GetEmailSettings()
                : null;
            if (model.FallbackSettings is null) useCustomSMTP = true;
            ViewBag.UseCustomSMTP = useCustomSMTP;
            if (useCustomSMTP)
            {
                model.Settings.Validate("Settings.", ModelState);
            }
            if (command == "Test")
            {
                try
                {
                    if (useCustomSMTP)
                    {
                        if (model.PasswordSet)
                        {
                            model.Settings.Password = store.GetStoreBlob().EmailSettings.Password;
                        }
                    }
                    
                    if (string.IsNullOrEmpty(model.TestEmail))
                        ModelState.AddModelError(nameof(model.TestEmail), new RequiredAttribute().FormatErrorMessage(nameof(model.TestEmail)));
                    if (!ModelState.IsValid)
                        return View(model);
                    var settings = useCustomSMTP ? model.Settings : model.FallbackSettings;
                    using var client = await settings.CreateSmtpClient();
                    var message = settings.CreateMailMessage(MailboxAddress.Parse(model.TestEmail), $"{store.StoreName}: Email test", "You received it, the BTCPay Server SMTP settings work.", false);
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                    TempData[WellKnownTempData.SuccessMessage] = $"Email sent to {model.TestEmail}. Please verify you received it.";
                }
                catch (Exception ex)
                {
                    TempData[WellKnownTempData.ErrorMessage] = "Error: " + ex.Message;
                }
                return View(model);
            }
            if (command == "ResetPassword")
            {
                var storeBlob = store.GetStoreBlob();
                storeBlob.EmailSettings.Password = null;
                store.SetStoreBlob(storeBlob);
                await _Repo.UpdateStore(store);
                TempData[WellKnownTempData.SuccessMessage] = "Email server password reset";
            }
            if (useCustomSMTP)
            {
                if (model.Settings.From is not null && !MailboxAddressValidator.IsMailboxAddress(model.Settings.From))
                {
                    ModelState.AddModelError("Settings.From", "Invalid email");
                }
                if (!ModelState.IsValid)
                    return View(model);
                var storeBlob = store.GetStoreBlob();
                if (storeBlob.EmailSettings != null && new EmailsViewModel(storeBlob.EmailSettings, model.FallbackSettings).PasswordSet)
                {
                    model.Settings.Password = storeBlob.EmailSettings.Password;
                }
                storeBlob.EmailSettings = model.Settings;
                store.SetStoreBlob(storeBlob);
                await _Repo.UpdateStore(store);
                TempData[WellKnownTempData.SuccessMessage] = "Email settings modified";
            }
            return RedirectToAction(nameof(StoreEmailSettings), new { storeId });
        }

        private static async Task<bool> IsSetupComplete(IEmailSender emailSender)
        {
            return emailSender is not null && (await emailSender.GetEmailSettings())?.IsComplete() == true;
        }
    }
}
