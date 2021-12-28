using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.PaymentRequests;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Security
{
    public class CookieAuthorizationHandler : AuthorizationHandler<PolicyRequirement>
    {
        private readonly HttpContext _httpContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;
        private readonly AppService _appService;
        private readonly PaymentRequestRepository _paymentRequestRepository;
        private readonly InvoiceRepository _invoiceRepository;

        public CookieAuthorizationHandler(IHttpContextAccessor httpContextAccessor,
                                UserManager<ApplicationUser> userManager,
                                StoreRepository storeRepository,
                                AppService appService,
                                InvoiceRepository invoiceRepository,
                                PaymentRequestRepository paymentRequestRepository)
        {
            _httpContext = httpContextAccessor.HttpContext;
            _userManager = userManager;
            _appService = appService;
            _storeRepository = storeRepository;
            _invoiceRepository = invoiceRepository;
            _paymentRequestRepository = paymentRequestRepository;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PolicyRequirement requirement)
        {
            if (context.User.Identity.AuthenticationType != AuthenticationSchemes.Cookie)
                return;
                
            var userId = _userManager.GetUserId(context.User);
            if (string.IsNullOrEmpty(userId))
                return;

            bool success = false;
            var isAdmin = context.User.IsInRole(Roles.ServerAdmin);

            AppData app = null;
            InvoiceEntity invoice = null;
            PaymentRequestData paymentRequest = null;
            string storeId;
            var explicitResource = false;
            if (context.Resource is string s)
            {
                explicitResource = true;
                storeId = s;
            }
            else
                storeId = _httpContext.GetImplicitStoreId();
            var routeData = _httpContext.GetRouteData();
            if (routeData != null)
            {
                // resolve from app
                if (routeData.Values.TryGetValue("appId", out var vAppId))
                {
                    string appId = vAppId as string;
                    app = await _appService.GetAppDataIfOwner(userId, appId);
                    if (storeId == null)
                    {
                        storeId = app?.StoreDataId;
                    }
                    else if (app?.StoreDataId != storeId)
                    {
                        app = null;
                    }
                }
                // resolve from payment request
                if (routeData.Values.TryGetValue("payReqId", out var vPayReqId))
                {
                    string payReqId = vPayReqId as string;
                    paymentRequest = await _paymentRequestRepository.FindPaymentRequest(payReqId, userId);
                    storeId ??= paymentRequest?.StoreDataId;
                }
                // resolve from invoice
                if (routeData.Values.TryGetValue("invoiceId", out var vInvoiceId))
                {
                    string invoiceId = vInvoiceId as string;
                    invoice = await _invoiceRepository.GetInvoice(invoiceId);
                    storeId ??= invoice?.StoreId;
                }
            }
            
            // Fall back to user prefs cookie
            var usedCookieFallback = false;
            if (storeId == null)
            {
                storeId = _httpContext.GetUserPrefsCookie()?.CurrentStoreId;
                usedCookieFallback = true;
            }

            var storeT = new AsyncLazy<StoreData>(() => storeId != null
                ? _storeRepository.FindStore(storeId, userId)
                : Task.FromResult<StoreData>(null));


            StoreData store;
            switch (requirement.Policy)
            {
                case Policies.CanModifyServerSettings:
                    if (isAdmin)
                        success = true;
                    break;
                case Policies.CanModifyStoreSettings:
                    store = await storeT.Value;
                    if (store != null && (store.Role == StoreRoles.Owner || isAdmin))
                        success = true;
                    break;
                case Policies.CanViewStoreSettings:
                    store = await storeT.Value;
                    if (store != null || isAdmin)
                        success = true;
                    break;
                case Policies.CanViewInvoices:
                    success = true;
                    if (usedCookieFallback)
                    {
                        storeId = null;
                    }
                    break;
                case Policies.CanCreateInvoice:
                    store = await storeT.Value;
                    if (store != null || isAdmin)
                        success = true;
                    break;
                case Policies.CanViewProfile:
                    if (context.User != null)
                        success = true;
                    break;
            }

            if (success)
            {
                context.Succeed(requirement);
                if (!explicitResource)
                {
                    store = await storeT.Value;
                    if (store != null)
                    {
                        _httpContext.SetStoreData(store);

                        // cache associated entities if present
                        if (app != null) _httpContext.SetAppData(app);
                        if (invoice != null) _httpContext.SetInvoiceData(invoice);
                        if (paymentRequest != null) _httpContext.SetPaymentRequestData(paymentRequest);
                    }
                }
            }
        }
    }
    
    public class AsyncLazy<T> : Lazy<Task<T>>
    {
        public AsyncLazy(Func<T> valueFactory) :
            base(() => Task.Factory.StartNew(valueFactory)) { }

        public AsyncLazy(Func<Task<T>> taskFactory) :
            base(() => Task.Factory.StartNew(taskFactory).Unwrap()) { }
    }
}
