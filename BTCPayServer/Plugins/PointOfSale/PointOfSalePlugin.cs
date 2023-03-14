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
using BTCPayServer.Services.Rates;
using Ganss.XSS;
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
            services.AddSingleton<IUIExtension>(new UIExtension("PointOfSale/NavExtension", "apps-nav"));
            services.AddSingleton<IApp,PointOfSaleApp>();
            base.Execute(services);
        }
    }
    
    public enum PosViewType
    {
        [Display(Name = "Product list")]
        Static,
        [Display(Name = "Product list with cart")]
        Cart,
        [Display(Name = "Keypad only")]
        Light,
        [Display(Name = "Print display")]
        Print
    }

    public class PointOfSaleApp: IApp
    {
        private readonly LinkGenerator _linkGenerator;
        private readonly IOptions<BTCPayServerOptions> _btcPayServerOptions;
        private readonly DisplayFormatter _displayFormatter;
        private readonly HtmlSanitizer _htmlSanitizer;
        public const string AppType = "PointOfSale";
        public string Description => "Point of Sale";
        public string Type => AppType;

        public PointOfSaleApp(
            LinkGenerator linkGenerator,
            IOptions<BTCPayServerOptions> btcPayServerOptions,
            DisplayFormatter displayFormatter,
            HtmlSanitizer htmlSanitizer)
        {
            _linkGenerator = linkGenerator;
            _btcPayServerOptions = btcPayServerOptions;
            _displayFormatter = displayFormatter;
            _htmlSanitizer = htmlSanitizer;
        }

        public string ConfigureLink(string appId)
        {
            return  _linkGenerator.GetPathByAction(nameof(UIPointOfSaleController.UpdatePointOfSale),
                "UIPointOfSale", new {appId}, _btcPayServerOptions.Value.RootPath);;
        }

        public Task<SalesStats> GetSaleStates(AppData app, InvoiceEntity[] paidInvoices, int numberOfDays)
        {
            var posS = app.GetSettings<PointOfSaleSettings>();
            var items = AppService.Parse(_htmlSanitizer, _displayFormatter, posS.Template, posS.Currency);
            return AppService.GetSalesStatswithPOSItems(items, paidInvoices, numberOfDays);
        }

        public Task<IEnumerable<ItemStats>> GetItemStats(AppData appData, InvoiceEntity[] paidInvoices)
        {
            var settings = appData.GetSettings<PointOfSaleSettings>();
            var items = AppService.Parse(_htmlSanitizer, _displayFormatter, settings.Template, settings.Currency);
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

        public Task<object> GetInfo(AppData appData)
        {
            throw new NotImplementedException();
        }

        public Task SetDefaultSettings(AppData appData, string defaultCurrency)
        {
            var empty = new PointOfSaleSettings { Currency = defaultCurrency };
            appData.SetSettings(empty);
            return Task.CompletedTask;
        }

        public string ViewLink(AppData app)
        {
            return _linkGenerator.GetPathByAction(nameof(UIPointOfSaleController.ViewPointOfSale),
                "UIPointOfSale", new { appId = app.Id }, _btcPayServerOptions.Value.RootPath);
        }
    }
}
