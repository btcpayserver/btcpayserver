using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services.Mails;
using BTCPayServer.Validation;
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
            var storeSetupComplete = blob.EmailSettings?.IsComplete() is true;
            if (!storeSetupComplete && !TempData.HasStatusMessage())
            {
                var emailSender = await _emailSenderFactory.GetEmailSender(store.Id) as StoreEmailSender;
                var hasServerFallback = await IsSetupComplete(emailSender?.FallbackSender);
                var message = hasServerFallback
                    ? "Emails will be sent with the email settings of the server"
                    : "You need to configure email settings before this feature works";
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = hasServerFallback ? StatusMessageModel.StatusSeverity.Info : StatusMessageModel.StatusSeverity.Warning,
                    Html = $"{message}. <a class='alert-link' href='{Url.Action("StoreEmailSettings", new { storeId })}'>Configure store email settings</a>."
                });
            }

            var vm = new StoreEmailRuleViewModel { Rules = blob.EmailRules ?? new List<StoreEmailRule>() };
            return View(vm);
        }

        [HttpPost("{storeId}/emails")]
        public async Task<IActionResult> StoreEmails(string storeId, StoreEmailRuleViewModel vm, string command)
        {
            vm.Rules ??= new List<StoreEmailRule>();
            int commandIndex = 0;
            var indSep = command.IndexOf(":", StringComparison.InvariantCultureIgnoreCase);
            if (indSep > 0)
            {
                var item = command[(indSep + 1)..];
                commandIndex = int.Parse(item, CultureInfo.InvariantCulture);
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
                if (!rule.CustomerEmail && string.IsNullOrEmpty(rule.To))
                    ModelState.AddModelError($"{nameof(vm.Rules)}[{i}].{nameof(rule.To)}", "Either recipient or \"Send the email to the buyer\" is required");
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
                        emailSender.SendEmail(MailboxAddress.Parse(rule.To), $"({store.StoreName} test) {rule.Subject}", rule.Body);
                        message += $"Test email sent to {rule.To} — please verify you received it.";
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
            
            [MailboxAddress]
            public string To { get; set; }
            
            [Required]
            public string Subject { get; set; }
            
            [Required]
            public string Body { get; set; }
        }

        [HttpGet("{storeId}/email-settings")]
        public IActionResult StoreEmailSettings()
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            var data = store.GetStoreBlob().EmailSettings ?? new EmailSettings();
            return View(new EmailsViewModel(data));
        }

        [HttpPost("{storeId}/email-settings")]
        public async Task<IActionResult> StoreEmailSettings(string storeId, EmailsViewModel model, string command)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            if (command == "Test")
            {
                try
                {
                    if (model.PasswordSet)
                    {
                        model.Settings.Password = store.GetStoreBlob().EmailSettings.Password;
                    }
                    model.Settings.Validate("Settings.", ModelState);
                    if (string.IsNullOrEmpty(model.TestEmail))
                        ModelState.AddModelError(nameof(model.TestEmail), new RequiredAttribute().FormatErrorMessage(nameof(model.TestEmail)));
                    if (!ModelState.IsValid)
                        return View(model);
                    using var client = await model.Settings.CreateSmtpClient();
                    var message = model.Settings.CreateMailMessage(MailboxAddress.Parse(model.TestEmail), "BTCPay test", "BTCPay test", false);
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
                return RedirectToAction(nameof(StoreEmailSettings), new { storeId });
            }
            else // if (command == "Save")
            {
                if (model.Settings.From is not null && !MailboxAddressValidator.IsMailboxAddress(model.Settings.From))
                {
                    ModelState.AddModelError("Settings.From", "Invalid email");
                    return View(model);
                }
                var storeBlob = store.GetStoreBlob();
                if (new EmailsViewModel(storeBlob.EmailSettings).PasswordSet && storeBlob.EmailSettings != null)
                {
                    model.Settings.Password = storeBlob.EmailSettings.Password;
                }
                storeBlob.EmailSettings = model.Settings;
                store.SetStoreBlob(storeBlob);
                await _Repo.UpdateStore(store);
                TempData[WellKnownTempData.SuccessMessage] = "Email settings modified";
                return RedirectToAction(nameof(StoreEmailSettings), new { storeId });
            }
        }

        private static async Task<bool> IsSetupComplete(IEmailSender emailSender)
        {
            return emailSender is not null && (await emailSender.GetEmailSettings())?.IsComplete() == true;
        }
    }
}
