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
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
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
            string storeId = context.Resource is string s ? s : _httpContext.GetImplicitStoreId();
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
            
            var store = storeId != null
                ? await _storeRepository.FindStore(storeId, userId)
                : null;

            switch (requirement.Policy)
            {
                case Policies.CanModifyServerSettings:
                    if (isAdmin)
                        success = true;
                    break;
                case Policies.CanModifyStoreSettings:
                    if (store != null && (store.Role == StoreRoles.Owner || isAdmin))
                        success = true;
                    break;
                case Policies.CanViewStoreSettings:
                    if (store != null || isAdmin)
                        success = true;
                    break;
                case Policies.CanCreateInvoice:
                    if (store != null || isAdmin)
                        success = true;
                    break;
                case Policies.CanViewProfile:
                    // FIXME: Is there a better way to check this?
                    var endpoint = context.Resource as RouteEndpoint;
                    if (endpoint?.RoutePattern?.RawText.IndexOf("Manage/", StringComparison.InvariantCultureIgnoreCase) == 0 || isAdmin)
                        success = true;
                    break;
            }

            if (success)
            {
                context.Succeed(requirement);
                
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
