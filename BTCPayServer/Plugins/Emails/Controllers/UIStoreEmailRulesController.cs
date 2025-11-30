using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Emails.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

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
    EmailTriggerViewModels triggers,
    IStringLocalizer stringLocalizer) : UIEmailRuleControllerBase(dbContextFactory, stringLocalizer, emailSenderFactory)
{
    [HttpGet("")]
    public Task<IActionResult> StoreEmailRulesList(string storeId)
        => EmailRulesListCore(CreateContext(storeId));

    private EmailsRuleControllerContext CreateContext(string storeId)
        => new()
        {
            StoreId = storeId,
            EmailSettingsLink = linkGenerator.GetStoreEmailSettingsLink(storeId, Request.GetRequestBaseUrl()),
            Rules = (ctx) => ctx.EmailRules.GetRules(storeId).ToListAsync(),
            Triggers = triggers.GetViewModels().Where(t => !t.ServerTrigger).ToList(),
            ModifyViewModel = (vm) =>
            {
                vm.ShowCustomerEmailColumn = true;
            },
            GetRule = (ctx, ruleId) => ctx.EmailRules.GetRule(storeId, ruleId),
            RedirectToRuleList = (redirectUrl) => GoToStoreEmailRulesList(storeId, redirectUrl)
        };

    [HttpGet("create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult StoreEmailRulesCreate(
        string storeId,
        string offeringId = null,
        string trigger = null,
        string condition = null,
        string to = null,
        string redirectUrl = null)
        => EmailRulesCreateCore(CreateContext(storeId), offeringId, trigger, condition, to, redirectUrl);

    [HttpPost("create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public Task<IActionResult> StoreEmailRulesCreate(string storeId, StoreEmailRuleViewModel model)
        => EmailRulesCreateCore(CreateContext(storeId), model);

    private IActionResult GoToStoreEmailRulesList(string storeId, string redirectUrl)
    {
        if (redirectUrl != null)
            return LocalRedirect(redirectUrl);
        return RedirectToAction(nameof(StoreEmailRulesList), new { storeId });
    }

    [HttpGet("{ruleId}/edit")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public Task<IActionResult> StoreEmailRulesEdit(string storeId, long ruleId, string redirectUrl = null)
        => EmailRulesEditCore(CreateContext(storeId), ruleId, redirectUrl);

    [HttpPost("{ruleId}/edit")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public Task<IActionResult> StoreEmailRulesEdit(string storeId, long ruleId, StoreEmailRuleViewModel model)
        => EmailRulesEditCore(CreateContext(storeId), ruleId, model);

    [HttpPost("{ruleId}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public Task<IActionResult> StoreEmailRulesDelete(string storeId, long ruleId, string redirectUrl = null)
        => EmailRulesDeleteCore(CreateContext(storeId), ruleId, redirectUrl);
}
