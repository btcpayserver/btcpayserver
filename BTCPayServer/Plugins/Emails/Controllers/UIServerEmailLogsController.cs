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

namespace BTCPayServer.Plugins.Emails.Controllers;

[Area(EmailsPlugin.Area)]
[Route("server/emails/logs")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIServerEmailLogsController(ApplicationDbContextFactory dbContextFactory, EmailSenderFactory emailSenderFactory)
    : UIEmailLogControllerBase(dbContextFactory, emailSenderFactory)
{
    [HttpGet("")]
    public Task<IActionResult> ServerEmailLogsList(int skip = 0, int count = 50) => EmailLogsListCore(CreateContext(), skip, count);

    [HttpPost("resend")]
    public Task<IActionResult> ResendServerEmails(string[] ids) => EmailLogsResendCore(CreateContext(), ids, null);

    private EmailLogsControllerContext CreateContext()
        => new()
        {
            StoreId = null,
            ModifyPermission = Policies.CanModifyServerSettings,
            LogsQuery = (ctx) => ctx.EmailLogs.AsQueryable().GetServerLogs().ToListAsync(),
            RedirectToLogsList = (redirectUrl) => redirectUrl != null ? LocalRedirect(redirectUrl) : RedirectToAction(nameof(ServerEmailLogsList))
        };
}
