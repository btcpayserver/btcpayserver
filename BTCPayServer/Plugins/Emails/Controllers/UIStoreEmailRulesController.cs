using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Services.Mails;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Npgsql;

namespace BTCPayServer.Plugins.Emails.Controllers;

[Area(EmailsPlugin.Area)]
[Route("stores/{storeId}/emails/rules")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIStoreEmailRulesController(
    EmailSenderFactory emailSenderFactory,
    LinkGenerator linkGenerator,
    ApplicationDbContextFactory dbContextFactory,
    IEnumerable<EmailTriggerViewModel> triggers,
    IStringLocalizer stringLocalizer) : Controller
{
    public IStringLocalizer StringLocalizer { get; set; } = stringLocalizer;
    [HttpGet("")]
    public async Task<IActionResult> StoreEmailRulesList(string storeId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var store = HttpContext.GetStoreData();
        var configured = await emailSenderFactory.IsComplete(store.Id);
        if (!configured && !TempData.HasStatusMessage())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Html = "You need to configure email settings before this feature works." +
                       $" <a class='alert-link configure-email' href='{linkGenerator.GetStoreEmailSettingsLink(storeId, Request.GetRequestBaseUrl())}'>Configure store email settings</a>."
            });
        }

        var rules = await ctx.EmailRules.GetRules(storeId).ToListAsync();
        return View("StoreEmailRulesList", rules.Select(r => new StoreEmailRuleViewModel(r, triggers)).ToList());
    }

    [HttpGet("create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult StoreEmailRulesCreate(string storeId)
    {
        return View("StoreEmailRulesManage", new StoreEmailRuleViewModel(null, triggers));
    }

    [HttpPost("create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> StoreEmailRulesCreate(string storeId, StoreEmailRuleViewModel model)
    {
        await ValidateCondition(model);
        if (!ModelState.IsValid)
            return StoreEmailRulesCreate(storeId);

        await using var ctx = dbContextFactory.CreateContext();
        var c = new EmailRuleData()
        {
            StoreId = storeId,
            Trigger = model.Trigger,
            Body = model.Body,
            Subject = model.Subject,
            Condition = string.IsNullOrWhiteSpace(model.Condition) ? null : model.Condition,
            To = model.ToAsArray()
        };
        c.SetBTCPayAdditionalData(model.AdditionalData);
        ctx.EmailRules.Add(c);
        await ctx.SaveChangesAsync();

        this.TempData.SetStatusSuccess(StringLocalizer["Email rule successfully created"]);
        return RedirectToAction(nameof(StoreEmailRulesList), new { storeId });
    }

    [HttpGet("{ruleId}/edit")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> StoreEmailRulesEdit(string storeId, long ruleId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var r = await ctx.EmailRules.GetRule(storeId, ruleId);
        if (r is null)
            return NotFound();
        return View("StoreEmailRulesManage", new StoreEmailRuleViewModel(r, triggers));
    }

    [HttpPost("{ruleId}/edit")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> StoreEmailRulesEdit(string storeId, long ruleId, StoreEmailRuleViewModel model)
    {
        await ValidateCondition(model);
        if (!ModelState.IsValid)
            return await StoreEmailRulesEdit(storeId, ruleId);

        await using var ctx = dbContextFactory.CreateContext();
        var rule = await ctx.EmailRules.GetRule(storeId, ruleId);
        if (rule is null) return NotFound();

        rule.Trigger = model.Trigger;
        rule.SetBTCPayAdditionalData(model.AdditionalData);
        rule.To = model.ToAsArray();
        rule.Subject = model.Subject;
        rule.Condition = model.Condition;
        rule.Body = model.Body;
        await ctx.SaveChangesAsync();

        this.TempData.SetStatusSuccess(StringLocalizer["Email rule successfully updated"]);
        return RedirectToAction(nameof(StoreEmailRulesList), new { storeId });
    }

    private async Task ValidateCondition(StoreEmailRuleViewModel model)
    {
        model.Condition = model.Condition?.Trim() ?? "";
        if (model.Condition.Length == 0)
            model.Condition = null;
        else
        {
            await using var ctx = dbContextFactory.CreateContext();
            try
            {
                ctx.Database
                    .GetDbConnection()
                    .ExecuteScalar<bool>("SELECT jsonb_path_exists('{}'::JSONB, @path::jsonpath)", new { path = model.Condition });
            }
            catch(PostgresException ex)
            {
                ModelState.AddModelError(nameof(model.Condition), $"Invalid condition ({ex.MessageText})");
            }
        }
    }

    [HttpPost("{ruleId}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> StoreEmailRulesDelete(string storeId, long ruleId)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var r = await ctx.EmailRules.GetRule(storeId, ruleId);
        if (r is not null)
        {
            ctx.EmailRules.Remove(r);
            await ctx.SaveChangesAsync();
            this.TempData.SetStatusSuccess(StringLocalizer["Email rule successfully deleted"]);
        }
        return RedirectToAction(nameof(StoreEmailRulesList), new { storeId });
    }
}
