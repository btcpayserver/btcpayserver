using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Plugins.Crowdfund;
using BTCPayServer.Plugins.Crowdfund.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Components.AppCrowdfund;

public class AppCrowdfund : ViewComponent
{
    private readonly AppService _appService;
    private readonly InvoiceRepository _invoiceRepository;
    private readonly DisplayFormatter _displayFormatter;
    private readonly IStringLocalizer _localizer;

    public AppCrowdfund(AppService appService, InvoiceRepository invoiceRepository, DisplayFormatter displayFormatter, IStringLocalizer stringLocalizer)
    {
        _appService = appService;
        _invoiceRepository = invoiceRepository;
        _displayFormatter = displayFormatter;
        _localizer = stringLocalizer;
    }

    public async Task<IViewComponentResult> InvokeAsync(string appId, string appType)
    {
        if (appType != CrowdfundAppType.AppType)
            return new HtmlContentViewComponentResult(new StringHtmlContent(string.Empty));

        var vm = new AppCrowdfundViewModel
        {
            Id = appId,
            AppType = appType,
            DataUrl = Url.Action("AppCrowdfund", "UIApps", new { appId }),
            InitialRendering = HttpContext.GetAppDataOrNull()?.Id != appId
        };
        if (vm.InitialRendering)
            return View(vm);

        var app = HttpContext.GetAppDataOrNull();
        if (app is null || _appService.GetAppType(appType) is not CrowdfundAppType type)
            return new HtmlContentViewComponentResult(new StringHtmlContent(string.Empty));

        var cf = (ViewCrowdfundViewModel)await type.GetInfo(app);
        var currency = cf.TargetCurrency;
        var currentAmount = cf.Info?.CurrentAmount ?? 0m;
        var contributions = cf.Info?.TotalContributors ?? 0;
        var perkCount = cf.PerkCount ?? new Dictionary<string, int>();

        vm.Name = app.Name;
        vm.Tagline = string.IsNullOrWhiteSpace(cf.Tagline) ? null : cf.Tagline;
        vm.Currency = currency;
        vm.Recurring = !cf.NeverReset;
        (vm.RecurrenceLabel, vm.PeriodNoun) = RecurrenceLabels(cf.ResetEvery);
        vm.Ended = cf.Ended;
        vm.Enabled = cf.Enabled;
        vm.Started = cf.Started;
        vm.HasTarget = cf.TargetAmount.HasValue;
        vm.ProgressPercentage = cf.Info?.ProgressPercentage;
        vm.GoalReached = (cf.Info?.ProgressPercentage ?? 0m) >= 100m;
        vm.Contributions = contributions;
        vm.EndDate = cf.EndDate;
        vm.ManageUrl = await type.ConfigureLink(app);
        vm.PublicUrl = await type.ViewLink(app);
        vm.ReportUrl = Url.Action("StoreReports", "UIReports", new { storeId = app.StoreDataId, viewName = "Sales" });

        vm.CurrentAmountFormatted = FormatAmount(currentAmount, currency);
        vm.CurrentAmountValue = FormatAmount(currentAmount, currency, DisplayFormatter.CurrencyFormat.None);
        if (cf.TargetAmount is decimal target)
        {
            vm.TargetAmountFormatted = FormatAmount(target, currency);
            var remaining = target - currentAmount;
            if (remaining > 0)
                vm.RemainingFormatted = FormatAmount(remaining, currency);
        }
        var now = DateTime.UtcNow;
        if (cf.EndDate is DateTime end && end > now)
            vm.DaysLeft = (int)Math.Ceiling((end - now).TotalDays);
        if (cf.Info?.NextResetDate is DateTime reset && reset > now)
            vm.RenewsInDays = (int)Math.Ceiling((reset - now).TotalDays);

        vm.Perks = (cf.Perks ?? Array.Empty<AppItem>())
            .Select(p => new AppCrowdfundViewModel.PerkStat
            {
                Title = string.IsNullOrWhiteSpace(p.Title) ? p.Id : p.Title,
                PriceFormatted = p.Price is decimal price ? FormatAmount(price, currency) : null,
                Count = perkCount.TryGetValue(p.Id, out var c) ? c : 0
            })
            .Where(p => p.Count > 0)
            .OrderByDescending(p => p.Count)
            .Take(5)
            .ToList();

        var paidStatuses = new[] { BTCPayServer.Client.Models.InvoiceStatus.Processing.ToString(), BTCPayServer.Client.Models.InvoiceStatus.Settled.ToString() };
        var paid = await AppService.GetInvoicesForApp(_invoiceRepository, app, status: paidStatuses);
        vm.RecentContributions = paid
            .OrderByDescending(i => i.InvoiceTime)
            .Take(3)
            .Select(i => new AppCrowdfundViewModel.Contribution
            {
                AmountFormatted = FormatAmount(i.Price, i.Currency),
                Date = i.InvoiceTime
            })
            .ToList();
        var largest = paid.OrderByDescending(i => i.Price).FirstOrDefault();
        if (largest != null)
            vm.LargestFormatted = FormatAmount(largest.Price, largest.Currency);

        return View(vm);
    }

    private (string label, string noun) RecurrenceLabels(string resetEvery) => resetEvery switch
    {
        "Hour" => (_localizer["hourly"].Value, _localizer["hour"].Value),
        "Day" => (_localizer["daily"].Value, _localizer["day"].Value),
        "Month" => (_localizer["monthly"].Value, _localizer["month"].Value),
        "Year" => (_localizer["yearly"].Value, _localizer["year"].Value),
        _ => (null, null)
    };

    // Format an amount trimmed of insignificant trailing zeros (e.g. 0.10000000 BTC -> 0.1 BTC).
    private string FormatAmount(decimal value, string currency, DisplayFormatter.CurrencyFormat format = DisplayFormatter.CurrencyFormat.Code)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        var significant = dot < 0 ? 0 : text[(dot + 1)..].TrimEnd('0').Length;
        return _displayFormatter.Currency(value, currency, format, significant);
    }
}
