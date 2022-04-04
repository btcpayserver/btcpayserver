using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services.Mails;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.VisualBasic;
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
            var data = blob.EmailSettings;
            if (data?.IsComplete() is not true)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Success,
                    Html =  $"You need to configure email settings before this feature works. <a class='alert-link' href='{this.Url.Action("StoreEmailSettings", new {storeId})}'>Configure now</a>. "
                });
                 return RedirectToAction();
            }
            return View( new StoreEmailRuleViewModel(){ Rules = blob.EmailRules??new List<StoreEmailRule>() });
        }

        
        

        [HttpPost("{storeId}/emails")]
        public async Task<IActionResult> StoreEmails(string storeId, StoreEmailRuleViewModel vm, string command)
        {
            vm.Rules ??= new List<StoreEmailRule>();
            if (command.StartsWith("remove", StringComparison.InvariantCultureIgnoreCase))
            {
                var index = int.Parse(
                    command.Substring(command.IndexOf(":", StringComparison.InvariantCultureIgnoreCase) + 1));
                vm.Rules.RemoveAt(index);
                
                return View(vm);
            }

            if (command == "add")
            {
                vm.Rules.Add(new StoreEmailRule());
                
                return View(vm);
            }
            if (!ModelState.IsValid)
            {
                return View(vm);
            }
            
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            var blob = store.GetStoreBlob();
            blob.EmailRules = vm.Rules;
            store.SetStoreBlob(blob);
            await _Repo.UpdateStore(store);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = $"Store email rules saved"
            });
            return RedirectToAction("StoreEmails", new {storeId});
        }

        public class StoreEmailRuleViewModel
        {
            public List<StoreEmailRule> Rules { get; set; }
        }
        public class StoreEmailRule
        {
            [Required]
            public WebhookEventType Trigger { get; set; }
            public bool CustomerEmail { get; set; }
            public string To { get; set; }
            public string CC { get; set; }
            public string BCC { get; set; }
            public string Body { get; set; }
            public string Subject { get; set; }
        }
        
        
        [Route("{storeId}/email-settings")]
        public IActionResult StoreEmailSettings()
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            var data = store.GetStoreBlob().EmailSettings ?? new EmailSettings();
            return View(new EmailsViewModel(data));
        }

        [Route("{storeId}/email-settings")]
        [HttpPost]
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
                    if (!model.Settings.IsComplete())
                    {
                        TempData[WellKnownTempData.ErrorMessage] = "Required fields missing";
                        return View(model);
                    }
                    using var client = await model.Settings.CreateSmtpClient();
                    var message = model.Settings.CreateMailMessage(new MailboxAddress(model.TestEmail, model.TestEmail), "BTCPay test", "BTCPay test", false);
                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                    TempData[WellKnownTempData.SuccessMessage] = "Email sent to " + model.TestEmail + ", please, verify you received it";
                }
                catch (Exception ex)
                {
                    TempData[WellKnownTempData.ErrorMessage] = "Error: " + ex.Message;
                }
                return View(model);
            }
            else if (command == "ResetPassword")
            {
                var storeBlob = store.GetStoreBlob();
                storeBlob.EmailSettings.Password = null;
                store.SetStoreBlob(storeBlob);
                await _Repo.UpdateStore(store);
                TempData[WellKnownTempData.SuccessMessage] = "Email server password reset";
                return RedirectToAction(nameof(StoreEmailSettings), new
                {
                    storeId
                });
            }
            else // if(command == "Save")
            {
                var storeBlob = store.GetStoreBlob();
                var oldPassword = storeBlob.EmailSettings?.Password;
                if (new EmailsViewModel(storeBlob.EmailSettings).PasswordSet)
                {
                    model.Settings.Password = storeBlob.EmailSettings.Password;
                }
                storeBlob.EmailSettings = model.Settings;
                store.SetStoreBlob(storeBlob);
                await _Repo.UpdateStore(store);
                TempData[WellKnownTempData.SuccessMessage] = "Email settings modified";
                return RedirectToAction(nameof(StoreEmailSettings), new
                {
                    storeId
                });
            }
        }
    }
}
