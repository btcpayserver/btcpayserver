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
using BTCPayServer.Models;
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
public class UIServerEmailLogsController(EmailLogRepository emailLogs, EmailSenderFactory emailSenderFactory, IStringLocalizer stringLocalizer)
    : UIEmailLogControllerBase(emailLogs, emailSenderFactory, stringLocalizer)
{
    [HttpGet("")]
    public Task<IActionResult> ServerEmailLogsList(int skip = 0, int count = BasePagingViewModel.CountDefault) => EmailLogsListCore(CreateContext(), skip, count);

    [HttpPost("resend")]
    public Task<IActionResult> ResendServerEmails(string[] ids) => EmailLogsResendCore(CreateContext(), ids, null);

    private EmailLogsControllerContext CreateContext()
        => new()
        {
            StoreId = null,
            ModifyPermission = Policies.CanModifyServerSettings,
            RedirectToLogsList = (redirectUrl) => redirectUrl != null ? LocalRedirect(redirectUrl) : RedirectToAction(nameof(ServerEmailLogsList))
        };
}
