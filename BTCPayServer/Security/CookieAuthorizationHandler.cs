using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.PaymentRequest;
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

            var isAdmin = context.User.IsInRole(Roles.ServerAdmin);
            switch (requirement.Policy)
            {
                case Policies.CanModifyServerSettings:
                    if (isAdmin)
                        context.Succeed(requirement);
                    return;
            }
                
            var userId = _userManager.GetUserId(context.User);
            if (string.IsNullOrEmpty(userId))
                return;

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
                    app = await _appService.GetApp(appId, null);
                    storeId ??= app?.StoreDataId;
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
            
            // store could not be found
            if (storeId == null)
            {
                return;
            }

            var store = await _storeRepository.FindStore(storeId, userId);
            
            bool success = false;
            switch (requirement.Policy)
            {
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
            }

            if (success)
            {
                context.Succeed(requirement);
                _httpContext.SetStoreData(store);
                
                // cache associated entities if present
                if (app != null) _httpContext.SetAppData(app);
                if (invoice != null) _httpContext.SetInvoiceData(invoice);
                if (paymentRequest != null) _httpContext.SetPaymentRequestData(paymentRequest);
            }
        }
    }
}
