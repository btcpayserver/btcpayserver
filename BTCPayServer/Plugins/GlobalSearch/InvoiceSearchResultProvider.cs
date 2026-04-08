using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.GlobalSearch.Views;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.GlobalSearch;

public class InvoiceSearchResultProvider(InvoiceRepository invoice,
    IStringLocalizer stringLocalizer) : ISearchResultItemProvider
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;
    const string Category = "Payments";
    public async Task ProvideAsync(SearchResultItemProviderContext context)
    {
        if (context is { UserQuery: string q, Store: not null })
        {
            var search = new SearchString(q);
            var invQuery = new InvoiceQuery();
            invQuery.FillFromSearchText(search, 0);
            invQuery.StoreId = [context.Store.Id];
            invQuery.UserId = context.UserId;
            foreach (var i in await invoice.GetInvoices(invQuery))
            {
                context.ItemResults.Add(new ResultItemViewModel()
                {
                    Category = Category,
                    Title = $"{StringLocalizer["Invoice"]} ❯ {Truncate(i.Id)}",
                    Url = context.Url.Action(nameof(UIInvoiceController.Invoice), "UIInvoice", new { invoiceId = i.Id }),
                    RequiredPolicy = Policies.CanViewInvoices
                });
            }
        }
        else if (context is { UserQuery: null, Store: StoreData store })
        {
            context.ItemResults.AddRange([
                new ResultItemViewModel
                {
                    Category = Category,
                    Title = "Invoices list",
                    Url = context.Url.Action(nameof(UIInvoiceController.ListInvoices), "UIInvoice", new { storeId = store.Id }),
                    Keywords = ["Payments", "List"],
                    RequiredPolicy = Policies.CanViewInvoices
                },
                new ResultItemViewModel
                {
                    Category = Category,
                    Title = "Create Invoice",
                    Url = context.Url.Action(nameof(UIInvoiceController.CreateInvoice), "UIInvoice", new { storeId = store.Id }),
                    Keywords = ["Invoice"],
                    RequiredPolicy = Policies.CanCreateInvoice
                }]);
        }
    }

    private string Truncate(string invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId) || invoiceId.Length <= 8)
            return invoiceId;
        return $"{invoiceId.Substring(0, 4)}...{invoiceId.Substring(invoiceId.Length - 3)}";
    }
}
