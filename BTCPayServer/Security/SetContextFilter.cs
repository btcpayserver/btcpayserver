#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Security;

public class SetContextFilter(
    PaymentRequestRepository paymentRequestRepository,
    InvoiceRepository invoiceRepository,
    AppService appService,
    StoreRepository storeRepository) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var userId = context.HttpContext.User.GetId();
        var isCookie = context.HttpContext.User.Identity is { AuthenticationType: AuthenticationSchemes.Cookie };

        if (httpContext.Items.TryGetValue(BuiltInPermissionHandler.StoreKey, out var oo) && oo is StoreData store)
        {
            httpContext.SetStoreData(store);
            if (isCookie)
                httpContext.SetNavStoreData(store);
        }
        else if (isCookie && httpContext.GetUserPrefsCookie()?.CurrentStoreId is string preferredStoreId)
        {
            var nav = httpContext.GetCachedStoreData(preferredStoreId);
            if (nav is null)
            {
                nav = await storeRepository.FindStore(preferredStoreId, httpContext.User, true);
                if (nav is not null)
                    httpContext.AddCachedStoreData(nav);
            }
            httpContext.SetNavStoreData(nav);
        }

        if (httpContext.Items.TryGetValue(BuiltInPermissionHandler.StoresKey, out var ooo) && ooo is StoreData[] stores)
            httpContext.SetStoresData(stores);

        if (httpContext.Items.TryGetValue(BuiltInPermissionScopeProvider.AdditionalScopeKey, out var o) && o is IEnumerable<BuiltInPermissionScopeProvider.AdditionalScope> additionalScopes)
        {
            foreach (var additionalScope in additionalScopes)
            {
                switch (additionalScope.ScopeName)
                {
                    case "appId":
                        var app = await appService.GetAppData(userId, additionalScope.Scope);
                        if (app is not null)
                            httpContext.SetAppData(app);
                        break;
                    case "payReqId":
                        var paymentRequest = await paymentRequestRepository.FindPaymentRequest(additionalScope.Scope, userId);
                        if (paymentRequest is not null)
                            httpContext.SetPaymentRequestData(paymentRequest);
                        break;
                    case "invoiceId":
                        var invoice = await invoiceRepository.GetInvoice(additionalScope.Scope);
                        if (invoice is not null)
                            httpContext.SetInvoiceData(invoice);
                        break;
                }
            }
        }

        await next();
    }
}
