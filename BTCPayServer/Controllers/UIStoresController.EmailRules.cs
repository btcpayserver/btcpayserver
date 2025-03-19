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
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class UIStoresController
    {
        [HttpGet("{storeId}/emails/rules")]
        public async Task<IActionResult> StoreEmailRulesList(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null) return NotFound();
            
            var configured = await _emailSenderFactory.IsComplete(store.Id);
            if (!configured && !TempData.HasStatusMessage())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Warning,
                    Html = "You need to configure email settings before this feature works." +
                           $" <a class='alert-link' href='{Url.Action("StoreEmailSettings", new { storeId })}'>Configure store email settings</a>."
                });
            }

            var rules = store.GetStoreBlob().EmailRules ?? new List<StoreEmailRule>();
            return View("StoreEmailRulesList", rules);
        }

        [HttpGet("{storeId}/emails/rules/create")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public IActionResult StoreEmailRulesCreate(string storeId)
        {
            return View("StoreEmailRulesManage", new StoreEmailRule());
        }

        [HttpPost("{storeId}/emails/rules/create")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> StoreEmailRulesCreate(string storeId, StoreEmailRule model)
        {
            if (!ModelState.IsValid)
                return View("StoreEmailRulesManage", model);

            var store = await _storeRepo.FindStore(storeId);
            if (store == null) return NotFound();

            var blob = store.GetStoreBlob();
            var rulesList = blob.EmailRules ?? new List<StoreEmailRule>();
            rulesList.Add(new StoreEmailRule
            {
                Trigger = model.Trigger,
                CustomerEmail = model.CustomerEmail,
                To = model.To,
                Subject = model.Subject,
                Body = model.Body
            });
            
            blob.EmailRules = rulesList;
            store.SetStoreBlob(blob);
            await _storeRepo.UpdateStore(store);

            return RedirectToAction(nameof(StoreEmailRulesList), new { storeId });
        }

        [HttpGet("{storeId}/emails/rules/{ruleIndex}/edit")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public IActionResult StoreEmailRulesEdit(string storeId, int ruleIndex)
        {
            var store = HttpContext.GetStoreData();
            if (store == null) return NotFound();

            var rules = store.GetStoreBlob().EmailRules;
            if (rules == null || ruleIndex >= rules.Count) return NotFound();

            var rule = rules[ruleIndex];
            return View("StoreEmailRulesManage", rule);
        }

        [HttpPost("{storeId}/emails/rules/{ruleIndex}/edit")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> StoreEmailRulesEdit(string storeId, int ruleIndex, StoreEmailRule model)
        {
            if (!ModelState.IsValid)
                return View("StoreEmailRulesManage", model);

            var store = await _storeRepo.FindStore(storeId);
            if (store == null) return NotFound();

            var blob = store.GetStoreBlob();
            if (blob.EmailRules == null || ruleIndex >= blob.EmailRules.Count) return NotFound();

            var rule = blob.EmailRules[ruleIndex];
            rule.Trigger = model.Trigger;
            rule.CustomerEmail = model.CustomerEmail;
            rule.To = model.To;
            rule.Subject = model.Subject;
            rule.Body = model.Body;
            store.SetStoreBlob(blob);
            await _storeRepo.UpdateStore(store);

            return RedirectToAction(nameof(StoreEmailRulesList), new { storeId });
        }

        [HttpPost("{storeId}/emails/rules/{ruleIndex}/delete")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> StoreEmailRulesDelete(string storeId, int ruleIndex)
        {
            var store = await _storeRepo.FindStore(storeId);
            if (store == null) return NotFound();

            var blob = store.GetStoreBlob();
            if (blob.EmailRules == null || ruleIndex >= blob.EmailRules.Count) return NotFound();

            blob.EmailRules.RemoveAt(ruleIndex);
            store.SetStoreBlob(blob);
            await _storeRepo.UpdateStore(store);

            return RedirectToAction(nameof(StoreEmailRulesList), new { storeId });
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
    }

}
