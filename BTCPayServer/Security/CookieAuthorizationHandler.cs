using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.PaymentRequest;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Security
{
    public class CookieAuthorizationHandler : AuthorizationHandler<PolicyRequirement>
    {
        private readonly HttpContext _HttpContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly StoreRepository _storeRepository;
        private readonly AppService _appService;
        private readonly PaymentRequestService _paymentRequestService;
        private readonly InvoiceRepository _invoiceRepository;

        public CookieAuthorizationHandler(IHttpContextAccessor httpContextAccessor,
                                UserManager<ApplicationUser> userManager,
                                StoreRepository storeRepository,
                                AppService appService,
                                InvoiceRepository invoiceRepository,
                                PaymentRequestService paymentRequestService)
        {
            _HttpContext = httpContextAccessor.HttpContext;
            _userManager = userManager;
            _appService = appService;
            _storeRepository = storeRepository;
            _invoiceRepository = invoiceRepository;
            _paymentRequestService = paymentRequestService;
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

            string storeId = context.Resource is string s ? s : _HttpContext.GetImplicitStoreId();
            if (storeId == null)
            {
                var routeData = _HttpContext.GetRouteData();
                if (routeData != null)
                {
                    // resolve from app
                    if (routeData.Values.TryGetValue("appId", out var vAppId))
                    {
                        string appId = vAppId as string;
                        var app = await _appService.GetApp(appId, null);
                        storeId = app?.StoreDataId;
                    }
                    // resolve from payment request
                    else if (routeData.Values.TryGetValue("payReqId", out var vPayReqId))
                    {
                        string payReqId = vPayReqId as string;
                        var paymentRequest = await _paymentRequestService.GetPaymentRequest(payReqId);
                        storeId = paymentRequest?.StoreId;
                    }
                    // resolve from app
                    if (routeData.Values.TryGetValue("invoiceId", out var vInvoiceId))
                    {
                        string invoiceId = vInvoiceId as string;
                        var invoice = await _invoiceRepository.GetInvoice(invoiceId);
                        storeId = invoice?.StoreId;
                    }
                }
                
                // store could not be found
                if (storeId == null)
                {
                    return;
                }
            }
                
            var userid = _userManager.GetUserId(context.User);
            if (string.IsNullOrEmpty(userid))
                return;

            var store = await _storeRepository.FindStore(storeId, userid);
            
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
                _HttpContext.SetStoreData(store);
                return;
            }
        }
    }
}
