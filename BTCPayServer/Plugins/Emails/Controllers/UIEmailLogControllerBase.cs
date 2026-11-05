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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using MimeKit;
using Npgsql;

namespace BTCPayServer.Plugins.Emails.Controllers;

public class UIEmailLogControllerBase(EmailLogRepository emailLogs, EmailSenderFactory emailSenderFactory, IStringLocalizer stringLocalizer) : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    public class EmailLogsControllerContext
    {
        public string? StoreId { get; set; }
        public string ModifyPermission { get; set; } = Client.Policies.CanModifyStoreSettings;
        public Func<string?, IActionResult> RedirectToLogsList = null!;
    }


    protected async Task<IActionResult> EmailLogsListCore(EmailLogsControllerContext context, int skip = 0, int count = BasePagingViewModel.CountDefault)
    {
        skip = Math.Max(skip, 0);
        count = Math.Clamp(count, 1, 500);
        var (items, total) = await emailLogs.GetPage(context.StoreId, skip, count);
        var vm = new EmailLogsViewModel
        {
            StoreId = context.StoreId,
            ModifyPermission = context.ModifyPermission,
            Skip = skip,
            Count = count,
            Total = total,
            Logs = items.ToList()
        };
        return View("EmailLogsList", vm);
    }

    protected async Task<IActionResult> EmailLogsResendCore(EmailLogsControllerContext context, string[] ids, string? redirectUrl)
    {
        if (ids is not { Length: > 0 })
        {
            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Select at least one email to resend."].Value;
            return context.RedirectToLogsList(redirectUrl);
        }
        var logs = await emailLogs.GetByIds(context.StoreId, ids);
        var sender = await emailSenderFactory.GetEmailSender(context.StoreId);
        foreach (var log in logs)
        {
            sender.SendEmail(ParseAddresses(log.To), ParseAddresses(log.CC), ParseAddresses(log.BCC), log.Subject, log.Body, context.StoreId, log.Trigger);
        }

        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Queued {0} email(s) for resend.", logs.Length].Value;
        return context.RedirectToLogsList(redirectUrl);
    }

    private static MailboxAddress[] ParseAddresses(string[]? addresses)
        => addresses?.Select(a => MailboxAddressValidator.TryParse(a, out var mb) ? mb : null).OfType<MailboxAddress>().ToArray() ?? Array.Empty<MailboxAddress>();
}

public class EmailLogsViewModel : BasePagingViewModel
{
    public string? StoreId { get; set; }
    public string ModifyPermission { get; set; } = null!;
    public List<EmailLogListItem> Logs { get; set; } = new();
    public override int CurrentPageCount => Logs.Count;
}
