#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Emails.Views.Shared;
using BTCPayServer.Plugins.Emails.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Npgsql;

namespace BTCPayServer.Plugins.Emails.Controllers;

public class UIEmailRuleControllerBase(
    ApplicationDbContextFactory dbContextFactory,
    IStringLocalizer stringLocalizer,
    EmailSenderFactory emailSenderFactory) : Controller
{
    public class EmailsRuleControllerContext
    {
        public string? StoreId { get; set; }
        public string EmailSettingsLink { get; set; } = "";
        public List<EmailTriggerViewModel> Triggers { get; set; } = new();
        public Func<ApplicationDbContext, Task<List<EmailRuleData>>>? Rules { get; set; }
        public Action<EmailRulesListViewModel>? ModifyViewModel { get; set; }

        public Func<ApplicationDbContext, long, Task<EmailRuleData?>>? GetRule { get; set; }

        public Func<string?, IActionResult> RedirectToRuleList = null!;
    }

    public ApplicationDbContextFactory DbContextFactory { get; } = dbContextFactory;
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    protected async Task<IActionResult> EmailRulesListCore(EmailsRuleControllerContext emailCtx)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var configured = await emailSenderFactory.IsComplete(emailCtx.StoreId);
        if (!configured && !TempData.HasStatusMessage())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Html = StringLocalizer["You need to configure email settings before this feature works. <a class='alert-link configure-email' href='{0}'>Configure email settings</a>.", HtmlEncoder.Default.Encode(emailCtx.EmailSettingsLink)]
            });
        }

        var rules = emailCtx.Rules is null ? new() : await emailCtx.Rules(ctx);
        var vm = new EmailRulesListViewModel()
        {
            StoreId = emailCtx.StoreId,
            ModifyPermission = Policies.CanModifyStoreSettings,
            Rules = rules.Select(r => new StoreEmailRuleViewModel(r, emailCtx.Triggers)).ToList()
        };
        if (emailCtx.ModifyViewModel is not null)
            emailCtx.ModifyViewModel(vm);
        return View("EmailRulesList", vm);
    }

    protected IActionResult EmailRulesCreateCore(
        EmailsRuleControllerContext ctx,
        string? offeringId = null,
        string? trigger = null,
        string? condition = null,
        string? to = null,
        string? redirectUrl = null)
    {
        return View("EmailRulesManage", new StoreEmailRuleViewModel(null, ctx.Triggers)
        {
            StoreId = ctx.StoreId,
            CanChangeTrigger = trigger is null,
            CanChangeCondition = offeringId is null,
            Condition = condition,
            Trigger = trigger,
            OfferingId = offeringId,
            RedirectUrl = redirectUrl,
            To = to,
            IsNew = true
        });
    }

    protected async Task<IActionResult> EmailRulesCreateCore(
        EmailsRuleControllerContext emailCtx,
        StoreEmailRuleViewModel model)
    {
        await ValidateCondition(model);
        if (!ModelState.IsValid)
            return EmailRulesCreateCore(emailCtx,
                model.OfferingId,
                model.CanChangeTrigger ? null : model.Trigger);

        await using var ctx = DbContextFactory.CreateContext();
        var c = new EmailRuleData()
        {
            StoreId = emailCtx.StoreId,
            Trigger = model.Trigger,
            Body = model.Body,
            Subject = model.Subject,
            Condition = string.IsNullOrWhiteSpace(model.Condition) ? null : model.Condition,
            OfferingId = model.OfferingId,
            To = model.AsArray(model.To),
            CC = model.AsArray(model.CC),
            BCC = model.AsArray(model.BCC),
        };
#pragma warning disable CS0618 // Type or member is obsolete
        // Safe, we just created the instance, nobody modifying at same time
        c.SetBTCPayAdditionalData(model.AdditionalData);
#pragma warning restore CS0618 // Type or member is obsolete
        ctx.EmailRules.Add(c);
        await ctx.SaveChangesAsync();

        this.TempData.SetStatusSuccess(StringLocalizer["Email rule successfully created"]);
        return emailCtx.RedirectToRuleList(model.RedirectUrl);
    }

    public async Task<IActionResult> EmailRulesEditCore(EmailsRuleControllerContext emailCtx, long ruleId, string? redirectUrl = null)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var r = emailCtx.GetRule is null ? null : await emailCtx.GetRule(ctx, ruleId);
        if (r is null)
            return NotFound();
        return View("EmailRulesManage", new StoreEmailRuleViewModel(r, emailCtx.Triggers)
        {
            CanChangeTrigger = r.OfferingId is null,
            CanChangeCondition = r.OfferingId is null,
            RedirectUrl = redirectUrl,
            IsNew = false
        });
    }

    public async Task<IActionResult> EmailRulesEditCore(EmailsRuleControllerContext emailCtx, long ruleId, StoreEmailRuleViewModel model)
    {
        await ValidateCondition(model);
        if (!ModelState.IsValid)
            return await EmailRulesEditCore(emailCtx, ruleId);

        await using var ctx = DbContextFactory.CreateContext();
        var rule = emailCtx.GetRule is null ? null : await emailCtx.GetRule(ctx, ruleId);
        if (rule is null) return NotFound();

        rule.Trigger = model.Trigger;
#pragma warning disable CS0618 // Type or member is obsolete
        // TODO: Do direct db update to avoid race condition
        rule.SetBTCPayAdditionalData(model.AdditionalData);
#pragma warning restore CS0618 // Type or member is obsolete
        rule.To = model.AsArray(model.To);
        rule.CC = model.AsArray(model.CC);
        rule.BCC = model.AsArray(model.BCC);
        rule.Subject = model.Subject;
        rule.Condition = model.Condition;
        rule.Body = model.Body;
        await ctx.SaveChangesAsync();

        this.TempData.SetStatusSuccess(StringLocalizer["Email rule successfully updated"]);
        return emailCtx.RedirectToRuleList(model.RedirectUrl);
    }

    protected async Task<IActionResult> EmailRulesDeleteCore(EmailsRuleControllerContext emailCtx, long ruleId, string? redirectUrl)
    {
        await using var ctx = DbContextFactory.CreateContext();
        var r = emailCtx.GetRule is null ? null : await emailCtx.GetRule(ctx, ruleId);
        if (r is not null)
        {
            ctx.EmailRules.Remove(r);
            await ctx.SaveChangesAsync();
            this.TempData.SetStatusSuccess(StringLocalizer["Email rule successfully deleted"]);
        }

        return emailCtx.RedirectToRuleList(redirectUrl);
    }


    protected async Task ValidateCondition(StoreEmailRuleViewModel model)
    {
        string[] modelKeys = [nameof(model.To), nameof(model.CC), nameof(model.BCC)];
        string[] values = [model.To, model.CC, model.BCC];
        for (int i = 0; i < modelKeys.Length; i++)
        {
            try
            {
                model.AsArray(values[i]);
            }
            catch (FormatException)
            {
                ModelState.AddModelError(modelKeys[i], StringLocalizer["Invalid email address or placeholder detected"]);
            }
        }

        model.Condition = model.Condition?.Trim() ?? "";
        if (model.Condition.Length == 0)
            model.Condition = null;
        else
        {
            await using var ctx = DbContextFactory.CreateContext();
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
}
