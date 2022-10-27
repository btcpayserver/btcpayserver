using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
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
        private readonly IPluginHookService _pluginHookService;

        public CookieAuthorizationHandler(IHttpContextAccessor httpContextAccessor,
                                UserManager<ApplicationUser> userManager,
                                StoreRepository storeRepository,
                                AppService appService,
                                InvoiceRepository invoiceRepository,
                                PaymentRequestRepository paymentRequestRepository,
                                IPluginHookService pluginHookService)
        {
            _httpContext = httpContextAccessor.HttpContext;
            _userManager = userManager;
            _appService = appService;
            _storeRepository = storeRepository;
            _invoiceRepository = invoiceRepository;
            _pluginHookService = pluginHookService;
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
            StoreData store = null;
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
                if (routeData.Values.TryGetValue("appId", out var vAppId) && vAppId is string appId)
                {
                    app = await _appService.GetAppDataIfOwner(userId, appId);
                    if (storeId == null)
                    {
                        storeId = app?.StoreDataId ?? String.Empty;
                    }
                    else if (app?.StoreDataId != storeId)
                    {
                        app = null;
                    }
                }
                // resolve from payment request
                if (routeData.Values.TryGetValue("payReqId", out var vPayReqId) && vPayReqId is string payReqId)
                {
                    paymentRequest = await _paymentRequestRepository.FindPaymentRequest(payReqId, userId);
                    if (storeId == null)
                    {
                        storeId = paymentRequest?.StoreDataId ?? String.Empty;
                    }
                    else if (paymentRequest?.StoreDataId != storeId)
                    {
                        paymentRequest = null;
                    }
                }
                // resolve from invoice
                if (routeData.Values.TryGetValue("invoiceId", out var vInvoiceId) && vInvoiceId is string invoiceId)
                {
                    invoice = await _invoiceRepository.GetInvoice(invoiceId);
                    if (storeId == null)
                    {
                        storeId = invoice?.StoreId ?? String.Empty;
                    }
                    else if (invoice?.StoreId != storeId)
                    {
                        invoice = null;
                    }
                }
            }

            // Fall back to user prefs cookie
            if (storeId == null)
            {
                storeId = _httpContext.GetUserPrefsCookie()?.CurrentStoreId;
            }

            if (string.IsNullOrEmpty(storeId))
                storeId = null;
            if (storeId != null)
            {
                store = await _storeRepository.FindStore(storeId, userId);
            }

            switch (requirement.Policy)
            {
                case Policies.CanModifyServerSettings:
                    if (isAdmin)
                        success = true;
                    break;
                case Policies.CanModifyStoreSettings:
                    if (store != null && (store.Role == StoreRoles.Owner))
                        success = true;
                    break;
                case Policies.CanViewInvoices:
                case Policies.CanViewStoreSettings:
                case Policies.CanCreateInvoice:
                    if (store != null)
                        success = true;
                    break;
                case Policies.CanViewProfile:
                case Policies.CanViewNotificationsForUser:
                case Policies.CanManageNotificationsForUser:
                case Policies.CanModifyStoreSettingsUnscoped:
                    if (context.User != null)
                        success = true;
                    break;
                default:
                    if (Policies.IsPluginPolicy(requirement.Policy))
                    {
                        var handle = (AuthorizationFilterHandle)await _pluginHookService.ApplyFilter("handle-authorization-requirement",
                            new AuthorizationFilterHandle(context, requirement, _httpContext));
                        success = handle.Success;
                    }
                    break;
            }

            if (success)
            {
                context.Succeed(requirement);
                if (!explicitResource)
                {
                    if (store != null)
                    {
                        _httpContext.SetStoreData(store);

                        // cache associated entities if present
                        if (app != null)
                            _httpContext.SetAppData(app);
                        if (invoice != null)
                            _httpContext.SetInvoiceData(invoice);
                        if (paymentRequest != null)
                            _httpContext.SetPaymentRequestData(paymentRequest);
                    }
                }
            }
        }
    }
}
