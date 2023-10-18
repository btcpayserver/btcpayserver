#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Plugins.PointOfSale.Controllers;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using Ganss.Xss;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.PointOfSale
{
    public class PointOfSalePlugin : BaseBTCPayServerPlugin
    {
        public override string Identifier => "BTCPayServer.Plugins.PointOfSale";
        public override string Name => "Point Of Sale";
        public override string Description => "Readily accept bitcoin without fees or a third-party, directly to your wallet.";

        public override void Execute(IServiceCollection services)
        {
            services.AddSingleton<IUIExtension>(new UIExtension("PointOfSale/NavExtension", "header-nav"));
            services.AddSingleton<AppBaseType, PointOfSaleAppType>();
            base.Execute(services);
        }
    }

    public enum PosViewType
    {
        [Display(Name = "Product list")]
        Static,
        [Display(Name = "Product list with cart")]
        Cart,
        [Display(Name = "Keypad")]
        Light,
        [Display(Name = "Print display")]
        Print
    }

    public class PointOfSaleAppType : AppBaseType, IHasSaleStatsAppType, IHasItemStatsAppType
    {
        private readonly LinkGenerator _linkGenerator;
        private readonly IOptions<BTCPayServerOptions> _btcPayServerOptions;
        private readonly DisplayFormatter _displayFormatter;
        public const string AppType = "PointOfSale";

        public PointOfSaleAppType(
            LinkGenerator linkGenerator,
            IOptions<BTCPayServerOptions> btcPayServerOptions,
            DisplayFormatter displayFormatter,
            HtmlSanitizer htmlSanitizer)
        {
            Type = AppType;
            Description = "Point of Sale";
            _linkGenerator = linkGenerator;
            _btcPayServerOptions = btcPayServerOptions;
            _displayFormatter = displayFormatter;
        }

        public override Task<string> ConfigureLink(AppData app)
        {
            return Task.FromResult(_linkGenerator.GetPathByAction(nameof(UIPointOfSaleController.UpdatePointOfSale),
                "UIPointOfSale", new { appId = app.Id }, _btcPayServerOptions.Value.RootPath)!);
        }

        public override Task<object?> GetInfo(AppData appData)
        {
            return Task.FromResult<object?>(null);
        }

        public Task<SalesStats> GetSalesStats(AppData app, InvoiceEntity[] paidInvoices, int numberOfDays)
        {
            var posS = app.GetSettings<PointOfSaleSettings>();
            var items = AppService.Parse(posS.Template);
            return AppService.GetSalesStatswithPOSItems(items, paidInvoices, numberOfDays);
        }

        public Task<IEnumerable<ItemStats>> GetItemStats(AppData appData, InvoiceEntity[] paidInvoices)
        {
            var settings = appData.GetSettings<PointOfSaleSettings>();
            var items = AppService.Parse(settings.Template);
            var itemCount = paidInvoices
                .Where(entity => entity.Currency.Equals(settings.Currency, StringComparison.OrdinalIgnoreCase) && (
                    // The POS data is present for the cart view, where multiple items can be bought
                    entity.Metadata.PosData is not null ||
                    // The item code should be present for all types other than the cart and keypad
                    !string.IsNullOrEmpty(entity.Metadata.ItemCode)
                ))
                .Aggregate(new List<AppService.InvoiceStatsItem>(), AppService.AggregateInvoiceEntitiesForStats(items))
                .GroupBy(entity => entity.ItemCode)
                .Select(entities =>
                {
                    var total = entities.Sum(entity => entity.FiatPrice);
                    var itemCode = entities.Key;
                    var item = items.FirstOrDefault(p => p.Id == itemCode);
                    return new ItemStats
                    {
                        ItemCode = itemCode,
                        Title = item?.Title ?? itemCode,
                        SalesCount = entities.Count(),
                        Total = total,
                        TotalFormatted = _displayFormatter.Currency(total, settings.Currency)
                    };
                })
                .OrderByDescending(stats => stats.SalesCount);

            return Task.FromResult<IEnumerable<ItemStats>>(itemCount);
        }

        public override Task SetDefaultSettings(AppData appData, string defaultCurrency)
        {
            var empty = new PointOfSaleSettings { Currency = defaultCurrency };
            appData.SetSettings(empty);
            return Task.CompletedTask;
        }

        public override Task<string> ViewLink(AppData app)
        {
            return Task.FromResult(_linkGenerator.GetPathByAction(nameof(UIPointOfSaleController.ViewPointOfSale),
                "UIPointOfSale", new { appId = app.Id }, _btcPayServerOptions.Value.RootPath)!);
        }
    }
}
