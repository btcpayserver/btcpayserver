#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Emails.Views.Shared;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using MimeKit;
using Npgsql;

namespace BTCPayServer.Plugins.Emails.Controllers;

[Area(EmailsPlugin.Area)]
[Route("stores/{storeId}/emails/logs")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIStoreEmailLogsController( ApplicationDbContextFactory dbContextFactory, EmailSenderFactory emailSenderFactory)
    : UIEmailLogControllerBase(dbContextFactory, emailSenderFactory)
{
    [HttpGet("")]
    public Task<IActionResult> StoreEmailLogsList(string storeId, int skip = 0, int count = 50) => EmailLogsListCore(CreateContext(storeId), skip, count);

    [HttpPost("resend")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public Task<IActionResult> ResendStoreEmails(string storeId, string[] ids) => EmailLogsResendCore(CreateContext(storeId), ids, null);

    private EmailLogsControllerContext CreateContext(string storeId)
        => new()
        {
            StoreId = storeId,
            ModifyPermission = Policies.CanModifyStoreSettings,
            LogsQuery = (ctx) => ctx.EmailLogs.AsQueryable().GetStoreLogs(storeId).ToListAsync(),
            RedirectToLogsList = (redirectUrl) => redirectUrl != null ? LocalRedirect(redirectUrl) : RedirectToAction(nameof(StoreEmailLogsList), new { storeId })
        };

}
