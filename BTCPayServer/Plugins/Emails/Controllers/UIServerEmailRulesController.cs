using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Emails.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Emails.Controllers;

[Area(EmailsPlugin.Area)]
[Route("server/rules")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIServerEmailRulesController(
    EmailSenderFactory emailSenderFactory,
    EmailTriggerViewModels triggers,
    ApplicationDbContextFactory dbContextFactory,
    IStringLocalizer stringLocalizer
    ) : UIEmailRuleControllerBase(dbContextFactory, stringLocalizer, emailSenderFactory)
{

    [HttpGet("")]
    public Task<IActionResult> ServerEmailRulesList()
        => EmailRulesListCore(CreateContext());

    private EmailsRuleControllerContext CreateContext()
        => new()
        {
            EmailSettingsLink = Url.Action(nameof(UIServerEmailController.ServerEmailSettings), "UIServerEmail") ?? throw new InvalidOperationException("Bug 1928"),
            Rules = (ctx) => ctx.EmailRules.GetServerRules().ToListAsync(),
            Triggers = triggers.GetViewModels().Where(t => t.ServerTrigger).ToList(),
            ModifyViewModel = (vm) =>
            {
                vm.ShowCustomerEmailColumn = false;
                vm.ModifyPermission = Policies.CanModifyServerSettings;
            },
            GetRule = (ctx, ruleId) => ctx.EmailRules.GetServerRule(ruleId),
            RedirectToRuleList = GoToServerRulesList
        };

    private IActionResult GoToServerRulesList(string redirectUrl)
    {
        if (redirectUrl != null)
            return LocalRedirect(redirectUrl);
        return RedirectToAction(nameof(ServerEmailRulesList));
    }

    [HttpGet("create")]
    public IActionResult ServerEmailRulesCreate(
        string trigger = null,
        string condition = null,
        string to = null,
        string redirectUrl = null)
        => EmailRulesCreateCore(CreateContext(), null, trigger, condition, to, redirectUrl);


    [HttpPost("create")]
    public Task<IActionResult> ServerEmailRulesCreate(StoreEmailRuleViewModel model)
        => EmailRulesCreateCore(CreateContext(), model);

    [HttpGet("{ruleId}/edit")]
    public Task<IActionResult> ServerEmailRulesEdit(long ruleId, string redirectUrl = null)
    => EmailRulesEditCore(CreateContext(), ruleId, redirectUrl);

    [HttpPost("{ruleId}/edit")]
    public Task<IActionResult> ServerEmailRulesEdit(long ruleId, StoreEmailRuleViewModel model)
    => EmailRulesEditCore(CreateContext(), ruleId, model);

    [HttpPost("{ruleId}/delete")]
    public Task<IActionResult> ServerEmailRulesDelete(long ruleId, string redirectUrl = null)
        => EmailRulesDeleteCore(CreateContext(), ruleId, redirectUrl);
}
