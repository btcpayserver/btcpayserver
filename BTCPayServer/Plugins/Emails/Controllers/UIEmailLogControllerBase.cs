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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using MimeKit;
using Npgsql;

namespace BTCPayServer.Plugins.Emails.Controllers;

public class UIEmailLogControllerBase(ApplicationDbContextFactory dbContextFactory, EmailSenderFactory emailSenderFactory) : Controller
{


    public class EmailLogsControllerContext
    {
        public string? StoreId { get; set; }
        public string ModifyPermission { get; set; } = Client.Policies.CanModifyStoreSettings;
        public Func<ApplicationDbContext, Task<List<EmailLogData>>> LogsQuery = null!;
        public Func<string?, IActionResult> RedirectToLogsList = null!;
    }


    protected async Task<IActionResult> EmailLogsListCore(EmailLogsControllerContext context, int skip = 0, int count = 50)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var all = await context.LogsQuery(ctx);
        var vm = new EmailLogsViewModel
        {
            StoreId = context.StoreId,
            ModifyPermission = context.ModifyPermission,
            Skip = skip,
            Count = count,
            Total = all.Count,
            Logs = all.Skip(skip).Take(count).ToList()
        };
        return View("EmailLogsList", vm);
    }

    protected async Task<IActionResult> EmailLogsResendCore(EmailLogsControllerContext context, string[] ids, string? redirectUrl)
    {
        if (ids is not { Length: > 0 })
        {
            TempData[WellKnownTempData.ErrorMessage] = "Select at least one email to resend.";
            return context.RedirectToLogsList(redirectUrl);
        }

        await using var ctx = dbContextFactory.CreateContext();
        var logs = await ctx.EmailLogs.Where(l => l.StoreId == context.StoreId && ids.Contains(l.Id)).ToListAsync();

        var sender = await emailSenderFactory.GetEmailSender(context.StoreId);
        var resent = 0;
        foreach (var log in logs)
        {
            var blob = log.GetBlob();
            if (blob is null)
                continue;
            sender.SendEmail(ParseAddresses(blob.To), ParseAddresses(blob.CC), ParseAddresses(blob.BCC), blob.Subject, blob.Body, context.StoreId, blob.Trigger);
            resent++;
        }

        TempData[WellKnownTempData.SuccessMessage] = $"Queued {resent} email(s) for resend.";
        return context.RedirectToLogsList(redirectUrl);
    }

    private static MailboxAddress[] ParseAddresses(string[]? addresses)
        => addresses?.Select(a => MailboxAddressValidator.TryParse(a, out var mb) ? mb : null).OfType<MailboxAddress>().ToArray() ?? Array.Empty<MailboxAddress>();
}

public class EmailLogsViewModel
{
    public string? StoreId { get; set; }
    public string ModifyPermission { get; set; } = null!;
    public int Skip { get; set; }
    public int Count { get; set; }
    public int Total { get; set; }
    public List<EmailLogData> Logs { get; set; } = new();
}
