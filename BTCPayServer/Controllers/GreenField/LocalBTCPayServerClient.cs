using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.GreenField;
using BTCPayServer.Data;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBXplorer.Models;
using InvoiceData = BTCPayServer.Client.Models.InvoiceData;
using Language = BTCPayServer.Client.Models.Language;
using NotificationData = BTCPayServer.Client.Models.NotificationData;
using PaymentRequestData = BTCPayServer.Client.Models.PaymentRequestData;
using PayoutData = BTCPayServer.Client.Models.PayoutData;
using PullPaymentData = BTCPayServer.Client.Models.PullPaymentData;
using StoreData = BTCPayServer.Client.Models.StoreData;
using StoreWebhookData = BTCPayServer.Client.Models.StoreWebhookData;
using WebhookDeliveryData = BTCPayServer.Client.Models.WebhookDeliveryData;

namespace BTCPayServer.Controllers.Greenfield
{
    public class BTCPayServerClientFactory : IBTCPayServerClientFactory
    {
        private readonly StoreRepository _storeRepository;
        private readonly IOptionsMonitor<IdentityOptions> _identityOptions;
        private readonly GreenfieldStoreOnChainPaymentMethodsController _chainPaymentMethodsController;
        private readonly GreenfieldStoreOnChainWalletsController _storeOnChainWalletsController;
        private readonly GreenfieldStoreLightningNetworkPaymentMethodsController _storeLightningNetworkPaymentMethodsController;
        private readonly GreenfieldStoreLNURLPayPaymentMethodsController _storeLnurlPayPaymentMethodsController;
        private readonly GreenfieldHealthController _healthController;
        private readonly GreenfieldPaymentRequestsController _paymentRequestController;
        private readonly GreenfieldApiKeysController _apiKeysController;
        private readonly GreenfieldNotificationsController _notificationsController;
        private readonly GreenfieldUsersController _usersController;
        private readonly GreenfieldStoresController _storesController;
        private readonly GreenfieldInternalLightningNodeApiController _internalLightningNodeApiController;
        private readonly GreenfieldStoreLightningNodeApiController _storeLightningNodeApiController;
        private readonly GreenfieldInvoiceController _greenFieldInvoiceController;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly GreenfieldServerInfoController _greenFieldServerInfoController;
        private readonly GreenfieldStoreWebhooksController _storeWebhooksController;
        private readonly GreenfieldPullPaymentController _greenfieldPullPaymentController;
        private readonly UIHomeController _homeController;
        private readonly GreenfieldStorePaymentMethodsController _storePaymentMethodsController;
        private readonly GreenfieldStoreEmailController _greenfieldStoreEmailController;
        private readonly IServiceProvider _serviceProvider;

        public BTCPayServerClientFactory(StoreRepository storeRepository,
            IOptionsMonitor<IdentityOptions> identityOptions,
            GreenfieldStoreOnChainPaymentMethodsController chainPaymentMethodsController,
            GreenfieldStoreOnChainWalletsController storeOnChainWalletsController,
            GreenfieldStoreLightningNetworkPaymentMethodsController storeLightningNetworkPaymentMethodsController,
            GreenfieldStoreLNURLPayPaymentMethodsController storeLnurlPayPaymentMethodsController,
            GreenfieldHealthController healthController,
            GreenfieldPaymentRequestsController paymentRequestController,
            GreenfieldApiKeysController apiKeysController,
            GreenfieldNotificationsController notificationsController,
            GreenfieldUsersController usersController,
            GreenfieldStoresController storesController,
            GreenfieldInternalLightningNodeApiController internalLightningNodeApiController,
            GreenfieldStoreLightningNodeApiController storeLightningNodeApiController,
            GreenfieldInvoiceController greenFieldInvoiceController,
            UserManager<ApplicationUser> userManager,
            GreenfieldServerInfoController greenFieldServerInfoController,
            GreenfieldStoreWebhooksController storeWebhooksController,
            GreenfieldPullPaymentController greenfieldPullPaymentController,
            UIHomeController homeController,
            GreenfieldStorePaymentMethodsController storePaymentMethodsController,
            GreenfieldStoreEmailController greenfieldStoreEmailController,
            IServiceProvider serviceProvider)
        {
            _storeRepository = storeRepository;
            _identityOptions = identityOptions;
            _chainPaymentMethodsController = chainPaymentMethodsController;
            _storeOnChainWalletsController = storeOnChainWalletsController;
            _storeLightningNetworkPaymentMethodsController = storeLightningNetworkPaymentMethodsController;
            _storeLnurlPayPaymentMethodsController = storeLnurlPayPaymentMethodsController;
            _healthController = healthController;
            _paymentRequestController = paymentRequestController;
            _apiKeysController = apiKeysController;
            _notificationsController = notificationsController;
            _usersController = usersController;
            _storesController = storesController;
            _internalLightningNodeApiController = internalLightningNodeApiController;
            _storeLightningNodeApiController = storeLightningNodeApiController;
            _greenFieldInvoiceController = greenFieldInvoiceController;
            _userManager = userManager;
            _greenFieldServerInfoController = greenFieldServerInfoController;
            _storeWebhooksController = storeWebhooksController;
            _greenfieldPullPaymentController = greenfieldPullPaymentController;
            _homeController = homeController;
            _storePaymentMethodsController = storePaymentMethodsController;
            _greenfieldStoreEmailController = greenfieldStoreEmailController;
            _serviceProvider = serviceProvider;
        }

        public async Task<BTCPayServerClient> Create(string userId, params string[] storeIds)
        {
            var context = new DefaultHttpContext();
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _userManager.FindByIdAsync(userId);
                List<Claim> claims = new List<Claim>
                {
                    new Claim(_identityOptions.CurrentValue.ClaimsIdentity.UserIdClaimType, userId),
                    new Claim(GreenfieldConstants.ClaimTypes.Permission,
                        Permission.Create(Policies.Unrestricted).ToString())
                };
                claims.AddRange((await _userManager.GetRolesAsync(user)).Select(s =>
                    new Claim(_identityOptions.CurrentValue.ClaimsIdentity.RoleClaimType, s)));
                context.User =
                    new ClaimsPrincipal(new ClaimsIdentity(claims, $"Local{GreenfieldConstants.AuthenticationType}WithUser"));
            }
            else
            {
                context.User =
                    new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>(), $"Local{GreenfieldConstants.AuthenticationType}"));
            }

            if (storeIds?.Any() is true)
            {
                context.SetStoreData(await _storeRepository.FindStore(storeIds.First()));
                context.SetStoresData(await _storeRepository.GetStoresByUserId(userId, storeIds));
            }
            else
            {
                context.SetStoresData(await _storeRepository.GetStoresByUserId(userId));
            }

            return new LocalBTCPayServerClient(
                _serviceProvider,
                _chainPaymentMethodsController,
                _storeOnChainWalletsController,
                _healthController,
                _paymentRequestController,
                _apiKeysController,
                _notificationsController,
                _usersController,
                _storesController,
                _storeLightningNodeApiController,
                _internalLightningNodeApiController,
                _storeLightningNetworkPaymentMethodsController,
                _storeLnurlPayPaymentMethodsController,
                _greenFieldInvoiceController,
                _greenFieldServerInfoController,
                _storeWebhooksController,
                _greenfieldPullPaymentController,
                _homeController,
                _storePaymentMethodsController,
                _greenfieldStoreEmailController,
                new LocalHttpContextAccessor() { HttpContext = context }
            );
        }
    }

    public class LocalHttpContextAccessor: IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    } 

    public class LocalBTCPayServerClient : BTCPayServerClient
    {
        private readonly GreenfieldStoreOnChainPaymentMethodsController _chainPaymentMethodsController;
        private readonly GreenfieldStoreOnChainWalletsController _storeOnChainWalletsController;
        private readonly GreenfieldHealthController _healthController;
        private readonly GreenfieldPaymentRequestsController _paymentRequestController;
        private readonly GreenfieldApiKeysController _apiKeysController;
        private readonly GreenfieldNotificationsController _notificationsController;
        private readonly GreenfieldUsersController _usersController;
        private readonly GreenfieldStoresController _storesController;
        private readonly GreenfieldStoreLightningNodeApiController _storeLightningNodeApiController;
        private readonly GreenfieldInternalLightningNodeApiController _lightningNodeApiController;
        private readonly GreenfieldStoreLightningNetworkPaymentMethodsController _storeLightningNetworkPaymentMethodsController;
        private readonly GreenfieldStoreLNURLPayPaymentMethodsController _storeLnurlPayPaymentMethodsController;
        private readonly GreenfieldInvoiceController _greenFieldInvoiceController;
        private readonly GreenfieldServerInfoController _greenFieldServerInfoController;
        private readonly GreenfieldStoreWebhooksController _storeWebhooksController;
        private readonly GreenfieldPullPaymentController _greenfieldPullPaymentController;
        private readonly UIHomeController _homeController;
        private readonly GreenfieldStorePaymentMethodsController _storePaymentMethodsController;
        private readonly GreenfieldStoreEmailController _greenfieldStoreEmailController;

        public LocalBTCPayServerClient(
            IServiceProvider serviceProvider,
            GreenfieldStoreOnChainPaymentMethodsController chainPaymentMethodsController,
            GreenfieldStoreOnChainWalletsController storeOnChainWalletsController,
            GreenfieldHealthController healthController,
            GreenfieldPaymentRequestsController paymentRequestController,
            GreenfieldApiKeysController apiKeysController,
            GreenfieldNotificationsController notificationsController,
            GreenfieldUsersController usersController,
            GreenfieldStoresController storesController,
            GreenfieldStoreLightningNodeApiController storeLightningNodeApiController,
            GreenfieldInternalLightningNodeApiController lightningNodeApiController,
            GreenfieldStoreLightningNetworkPaymentMethodsController storeLightningNetworkPaymentMethodsController,
            GreenfieldStoreLNURLPayPaymentMethodsController storeLnurlPayPaymentMethodsController,
            GreenfieldInvoiceController greenFieldInvoiceController,
            GreenfieldServerInfoController greenFieldServerInfoController,
            GreenfieldStoreWebhooksController storeWebhooksController,
            GreenfieldPullPaymentController greenfieldPullPaymentController,
            UIHomeController homeController,
            GreenfieldStorePaymentMethodsController storePaymentMethodsController,
            GreenfieldStoreEmailController greenfieldStoreEmailController,
            IHttpContextAccessor httpContextAccessor) : base(new Uri("https://dummy.local"), "", "")
        {
            _chainPaymentMethodsController = chainPaymentMethodsController;
            _storeOnChainWalletsController = storeOnChainWalletsController;
            _healthController = healthController;
            _paymentRequestController = paymentRequestController;
            _apiKeysController = apiKeysController;
            _notificationsController = notificationsController;
            _usersController = usersController;
            _storesController = storesController;
            _storeLightningNodeApiController = storeLightningNodeApiController;
            _lightningNodeApiController = lightningNodeApiController;
            _storeLightningNetworkPaymentMethodsController = storeLightningNetworkPaymentMethodsController;
            _storeLnurlPayPaymentMethodsController = storeLnurlPayPaymentMethodsController;
            _greenFieldInvoiceController = greenFieldInvoiceController;
            _greenFieldServerInfoController = greenFieldServerInfoController;
            _storeWebhooksController = storeWebhooksController;
            _greenfieldPullPaymentController = greenfieldPullPaymentController;
            _homeController = homeController;
            _storePaymentMethodsController = storePaymentMethodsController;
            _greenfieldStoreEmailController = greenfieldStoreEmailController;

            var controllers = new[]
            {
                chainPaymentMethodsController, storeOnChainWalletsController, healthController,
                paymentRequestController, apiKeysController, notificationsController, usersController,
                storeLightningNetworkPaymentMethodsController, greenFieldInvoiceController, storeWebhooksController,
                greenFieldServerInfoController, greenfieldPullPaymentController, storesController, homeController,
                lightningNodeApiController, storeLightningNodeApiController as ControllerBase, storePaymentMethodsController, 
                greenfieldStoreEmailController
            };

            var authoverride = new DefaultAuthorizationService(
                serviceProvider.GetRequiredService<IAuthorizationPolicyProvider>(),
                new AuthHandlerProvider(
                    serviceProvider.GetRequiredService<StoreRepository>(), 
                    serviceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                    httpContextAccessor
                ),
                serviceProvider.GetRequiredService<ILogger<DefaultAuthorizationService>>(),
                serviceProvider.GetRequiredService<IAuthorizationHandlerContextFactory>(),
                serviceProvider.GetRequiredService<IAuthorizationEvaluator>(),
                serviceProvider.GetRequiredService<IOptions<AuthorizationOptions>>()


            );
            
            
            foreach (var controller in controllers)
            {
                controller.ControllerContext.HttpContext = httpContextAccessor.HttpContext;
                var authInterface = typeof(IAuthorizationService);
                foreach (FieldInfo fieldInfo in controller.GetType().GetFields().Where(info => authInterface.IsAssignableFrom(info.FieldType)))
                {
                    fieldInfo.SetValue(controller, authoverride);
                }
            }
        }

        class AuthHandlerProvider : IAuthorizationHandlerProvider
        {
            private readonly IHttpContextAccessor _httpContextAccessor;


            private readonly UserManager<ApplicationUser> _userManager;
            private readonly StoreRepository _storeRepository;

            public AuthHandlerProvider(StoreRepository storeRepository, UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
            {
                _storeRepository = storeRepository;
                _userManager = userManager;
                _httpContextAccessor = httpContextAccessor;
            }
            public Task<IEnumerable<IAuthorizationHandler>> GetHandlersAsync(AuthorizationHandlerContext context)
            {
                return Task.FromResult<IEnumerable<IAuthorizationHandler>>(new IAuthorizationHandler[]
                {
                    new LocalGreenfieldAuthorizationHandler(_httpContextAccessor, _userManager, _storeRepository)
                });
            }
        }
        protected override HttpRequestMessage CreateHttpRequest(string path,
            Dictionary<string, object> queryPayload = null, HttpMethod method = null)
        {
            throw new NotSupportedException("This method is not supported by the LocalBTCPayServerClient.");
        }

        public override async Task<StoreWebhookData> CreateWebhook(string storeId, CreateStoreWebhookRequest create,
            CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData>(
                await _storeWebhooksController.CreateWebhook(storeId, create));
        }

        public override async Task<StoreWebhookData> GetWebhook(string storeId, string webhookId,
            CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData>(
                await _storeWebhooksController.ListWebhooks(storeId, webhookId));
        }

        public override async Task<StoreWebhookData> UpdateWebhook(string storeId, string webhookId,
            UpdateStoreWebhookRequest update,
            CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData>(
                await _storeWebhooksController.UpdateWebhook(storeId, webhookId, update));
        }

        public override async Task<bool> DeleteWebhook(string storeId, string webhookId,
            CancellationToken token = default)
        {
            HandleActionResult(await _storeWebhooksController.DeleteWebhook(storeId, webhookId));
            return true;
        }

        public override async Task<StoreWebhookData[]> GetWebhooks(string storeId, CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData[]>(
                await _storeWebhooksController.ListWebhooks(storeId, null));
        }

        public override async Task<WebhookDeliveryData[]> GetWebhookDeliveries(string storeId, string webhookId,
            CancellationToken token = default)
        {
            return GetFromActionResult<WebhookDeliveryData[]>(
                await _storeWebhooksController.ListDeliveries(storeId, webhookId, null));
        }

        public override async Task<WebhookDeliveryData> GetWebhookDelivery(string storeId, string webhookId,
            string deliveryId, CancellationToken token = default)
        {
            return GetFromActionResult<WebhookDeliveryData>(
                await _storeWebhooksController.ListDeliveries(storeId, webhookId, deliveryId));
        }

        public override async Task<string> RedeliverWebhook(string storeId, string webhookId, string deliveryId,
            CancellationToken token = default)
        {
            return GetFromActionResult<string>(
                await _storeWebhooksController.RedeliverWebhook(storeId, webhookId, deliveryId));
        }

        public override async Task<WebhookEvent> GetWebhookDeliveryRequest(string storeId, string webhookId,
            string deliveryId, CancellationToken token = default)
        {
            return GetFromActionResult<WebhookEvent>(
                await _storeWebhooksController.GetDeliveryRequest(storeId, webhookId, deliveryId));
        }

        public override async Task<PullPaymentData> CreatePullPayment(string storeId, CreatePullPaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PullPaymentData>(
                await _greenfieldPullPaymentController.CreatePullPayment(storeId, request));
        }

        public override async Task<PullPaymentData> GetPullPayment(string pullPaymentId,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PullPaymentData>(
                await _greenfieldPullPaymentController.GetPullPayment(pullPaymentId));
        }

        public override async Task<PullPaymentData[]> GetPullPayments(string storeId, bool includeArchived = false,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PullPaymentData[]>(
                await _greenfieldPullPaymentController.GetPullPayments(storeId, includeArchived));
        }

        public override async Task ArchivePullPayment(string storeId, string pullPaymentId,
            CancellationToken cancellationToken = default)
        {
            HandleActionResult(await _greenfieldPullPaymentController.ArchivePullPayment(storeId, pullPaymentId));
        }

        public override async Task<PayoutData[]> GetPayouts(string pullPaymentId, bool includeCancelled = false,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData[]>(
                await _greenfieldPullPaymentController.GetPayouts(pullPaymentId, includeCancelled));
        }

        public override async Task<PayoutData> CreatePayout(string pullPaymentId, CreatePayoutRequest payoutRequest,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData>(
                await _greenfieldPullPaymentController.CreatePayout(pullPaymentId, payoutRequest));
        }

        public override async Task CancelPayout(string storeId, string payoutId,
            CancellationToken cancellationToken = default)
        {
            HandleActionResult(await _greenfieldPullPaymentController.CancelPayout(storeId, payoutId));
        }

        public override async Task<PayoutData> ApprovePayout(string storeId, string payoutId,
            ApprovePayoutRequest request,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData>(
                await _greenfieldPullPaymentController.ApprovePayout(storeId, payoutId, request, cancellationToken));
        }

        public override async Task<LightningNodeInformationData> GetLightningNodeInfo(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningNodeInformationData>(
                await _storeLightningNodeApiController.GetInfo(cryptoCode));
        }

        public override async Task ConnectToLightningNode(string storeId, string cryptoCode,
            ConnectToNodeRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await _storeLightningNodeApiController.ConnectToNode(cryptoCode, request));
        }

        public override async Task<IEnumerable<LightningChannelData>> GetLightningNodeChannels(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<LightningChannelData>>(
                await _storeLightningNodeApiController.GetChannels(cryptoCode));
        }

        public override async Task OpenLightningChannel(string storeId, string cryptoCode,
            OpenLightningChannelRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await _storeLightningNodeApiController.OpenChannel(cryptoCode, request));
        }

        public override async Task<string> GetLightningDepositAddress(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<string>(
                await _storeLightningNodeApiController.GetDepositAddress(cryptoCode));
        }

        public override async Task PayLightningInvoice(string storeId, string cryptoCode,
            PayLightningInvoiceRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await _storeLightningNodeApiController.PayInvoice(cryptoCode, request));
        }

        public override async Task<LightningInvoiceData> GetLightningInvoice(string storeId, string cryptoCode,
            string invoiceId, CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await _storeLightningNodeApiController.GetInvoice(cryptoCode, invoiceId));
        }

        public override async Task<LightningInvoiceData> CreateLightningInvoice(string storeId, string cryptoCode,
            CreateLightningInvoiceRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await _storeLightningNodeApiController.CreateInvoice(cryptoCode, request));
        }

        public override async Task<LightningNodeInformationData> GetLightningNodeInfo(string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningNodeInformationData>(
                await _lightningNodeApiController.GetInfo(cryptoCode));
        }

        public override async Task ConnectToLightningNode(string cryptoCode, ConnectToNodeRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await _lightningNodeApiController.ConnectToNode(cryptoCode, request));
        }

        public override async Task<IEnumerable<LightningChannelData>> GetLightningNodeChannels(string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<LightningChannelData>>(
                await _lightningNodeApiController.GetChannels(cryptoCode));
        }

        public override async Task OpenLightningChannel(string cryptoCode, OpenLightningChannelRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await _lightningNodeApiController.OpenChannel(cryptoCode, request));
        }

        public override async Task<string> GetLightningDepositAddress(string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<string>(
                await _lightningNodeApiController.GetDepositAddress(cryptoCode));
        }

        public override async Task<LightningPaymentData> PayLightningInvoice(string cryptoCode, PayLightningInvoiceRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningPaymentData>(
                await _lightningNodeApiController.PayInvoice(cryptoCode, request));
        }

        public override async Task<LightningInvoiceData> GetLightningInvoice(string cryptoCode, string invoiceId,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await _lightningNodeApiController.GetInvoice(cryptoCode, invoiceId));
        }

        public override async Task<LightningInvoiceData> CreateLightningInvoice(string cryptoCode,
            CreateLightningInvoiceRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await _lightningNodeApiController.CreateInvoice(cryptoCode, request));
        }

        private T GetFromActionResult<T>(IActionResult result)
        {
            HandleActionResult(result);
            switch (result)
            {
                case JsonResult jsonResult:
                    return (T)jsonResult.Value;
                case OkObjectResult { Value: T res }:
                    return res;
                default:
                    return default;
            }
        }

        private void HandleActionResult(IActionResult result)
        {
            switch (result)
            {
                case UnprocessableEntityObjectResult { Value: List<GreenfieldValidationError> validationErrors }:
                    throw new GreenfieldValidationException(validationErrors.ToArray());
                case BadRequestObjectResult { Value: GreenfieldAPIError error }:
                    throw new GreenfieldAPIException(400, error);
                case NotFoundResult _:
                    throw new GreenfieldAPIException(404, new GreenfieldAPIError("not-found", ""));
                default:
                    return;
            }
        }

        private T GetFromActionResult<T>(ActionResult result)
        {
            return GetFromActionResult<T>((IActionResult)result);
        }

        private T GetFromActionResult<T>(ActionResult<T> result)
        {
            return result.Value ?? GetFromActionResult<T>(result.Result);
        }

        public override Task<IEnumerable<OnChainPaymentMethodData>> GetStoreOnChainPaymentMethods(string storeId,
            bool? enabled, CancellationToken token)
        {
            return Task.FromResult(GetFromActionResult(_chainPaymentMethodsController.GetOnChainPaymentMethods(storeId, enabled)));
        }

        public override Task<OnChainPaymentMethodData> GetStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult(
                _chainPaymentMethodsController.GetOnChainPaymentMethod(storeId, cryptoCode)));
        }

        public override async Task RemoveStoreOnChainPaymentMethod(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            HandleActionResult(await _chainPaymentMethodsController.RemoveOnChainPaymentMethod(storeId, cryptoCode));
        }

        public override async Task<OnChainPaymentMethodData> UpdateStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, UpdateOnChainPaymentMethodRequest paymentMethod,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainPaymentMethodData>(
                await _chainPaymentMethodsController.UpdateOnChainPaymentMethod(storeId, cryptoCode, new UpdateOnChainPaymentMethodRequest(
                    enabled: paymentMethod.Enabled,
                    label: paymentMethod.Label,
                    accountKeyPath: paymentMethod.AccountKeyPath,
                    derivationScheme: paymentMethod.DerivationScheme
                )));
        }

        public override Task<OnChainPaymentMethodPreviewResultData> PreviewProposedStoreOnChainPaymentMethodAddresses(
            string storeId, string cryptoCode,
            UpdateOnChainPaymentMethodRequest paymentMethod, int offset = 0, int amount = 10, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<OnChainPaymentMethodPreviewResultData>(
                _chainPaymentMethodsController.GetProposedOnChainPaymentMethodPreview(storeId, cryptoCode,
                    paymentMethod, offset, amount)));
        }

        public override Task<OnChainPaymentMethodPreviewResultData> PreviewStoreOnChainPaymentMethodAddresses(
            string storeId, string cryptoCode, int offset = 0, int amount = 10,
            CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<OnChainPaymentMethodPreviewResultData>(
                    _chainPaymentMethodsController.GetOnChainPaymentMethodPreview(storeId, cryptoCode, offset,
                    amount)));
        }

        public override Task<ApiHealthData> GetHealth(CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<ApiHealthData>(_healthController.GetHealth()));
        }

        public override async Task<IEnumerable<PaymentRequestData>> GetPaymentRequests(string storeId,
            bool includeArchived = false, CancellationToken token = default)
        {
            return GetFromActionResult(await _paymentRequestController.GetPaymentRequests(storeId, includeArchived));
        }

        public override async Task<PaymentRequestData> GetPaymentRequest(string storeId, string paymentRequestId,
            CancellationToken token = default)
        {
            return GetFromActionResult<PaymentRequestData>(await _paymentRequestController.GetPaymentRequest(storeId, paymentRequestId));
        }

        public override async Task ArchivePaymentRequest(string storeId, string paymentRequestId,
            CancellationToken token = default)
        {
            HandleActionResult(await _paymentRequestController.ArchivePaymentRequest(storeId, paymentRequestId));
        }

        public override async Task<PaymentRequestData> CreatePaymentRequest(string storeId,
            CreatePaymentRequestRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<PaymentRequestData>(
                await _paymentRequestController.CreatePaymentRequest(storeId, request));
        }

        public override async Task<PaymentRequestData> UpdatePaymentRequest(string storeId, string paymentRequestId,
            UpdatePaymentRequestRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<PaymentRequestData>(
                await _paymentRequestController.UpdatePaymentRequest(storeId, paymentRequestId, request));
        }

        public override async Task<ApiKeyData> GetCurrentAPIKeyInfo(CancellationToken token = default)
        {
            return GetFromActionResult<ApiKeyData>(await _apiKeysController.GetKey());
        }

        public override async Task<ApiKeyData> CreateAPIKey(CreateApiKeyRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<ApiKeyData>(await _apiKeysController.CreateKey(request));
        }

        public override async Task RevokeCurrentAPIKeyInfo(CancellationToken token = default)
        {
            HandleActionResult(await _apiKeysController.RevokeCurrentKey());
        }

        public override async Task RevokeAPIKey(string apikey, CancellationToken token = default)
        {
            HandleActionResult(await _apiKeysController.RevokeKey(apikey));
        }

        public override async Task<IEnumerable<NotificationData>> GetNotifications(bool? seen = null,
            int? skip = null, int? take = null, CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<NotificationData>>(
                await _notificationsController.GetNotifications(seen, skip, take));
        }

        public override async Task<NotificationData> GetNotification(string notificationId,
            CancellationToken token = default)
        {
            return GetFromActionResult<NotificationData>(
                await _notificationsController.GetNotification(notificationId));
        }

        public override async Task<NotificationData> UpdateNotification(string notificationId, bool? seen,
            CancellationToken token = default)
        {
            return GetFromActionResult<NotificationData>(
                await _notificationsController.UpdateNotification(notificationId,
                    new UpdateNotification() { Seen = seen }));
        }

        public override async Task RemoveNotification(string notificationId, CancellationToken token = default)
        {
            HandleActionResult(await _notificationsController.DeleteNotification(notificationId));
        }

        public override async Task<ApplicationUserData> GetCurrentUser(CancellationToken token = default)
        {
            return GetFromActionResult(await _usersController.GetCurrentUser());
        }

        public override async Task<ApplicationUserData> CreateUser(CreateApplicationUserRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<ApplicationUserData>(await _usersController.CreateUser(request, token));
        }

        public override async Task<OnChainWalletOverviewData> ShowOnChainWalletOverview(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletOverviewData>(
                await _storeOnChainWalletsController.ShowOnChainWalletOverview(storeId, cryptoCode));
        }

        public override async Task<OnChainWalletAddressData> GetOnChainWalletReceiveAddress(string storeId,
            string cryptoCode, bool forceGenerate = false,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletAddressData>(
                await _storeOnChainWalletsController.GetOnChainWalletReceiveAddress(storeId, cryptoCode,
                    forceGenerate));
        }

        public override async Task UnReserveOnChainWalletReceiveAddress(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            HandleActionResult(
                await _storeOnChainWalletsController.UnReserveOnChainWalletReceiveAddress(storeId, cryptoCode));
        }

        public override async Task<IEnumerable<OnChainWalletTransactionData>> ShowOnChainWalletTransactions(
            string storeId, string cryptoCode, TransactionStatus[] statusFilter = null,
            CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<OnChainWalletTransactionData>>(
                await _storeOnChainWalletsController.ShowOnChainWalletTransactions(storeId, cryptoCode, statusFilter));
        }

        public override async Task<OnChainWalletTransactionData> GetOnChainWalletTransaction(string storeId,
            string cryptoCode, string transactionId,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletTransactionData>(
                await _storeOnChainWalletsController.GetOnChainWalletTransaction(storeId, cryptoCode, transactionId));
        }

        public override async Task<IEnumerable<OnChainWalletUTXOData>> GetOnChainWalletUTXOs(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<OnChainWalletUTXOData>>(
                await _storeOnChainWalletsController.GetOnChainWalletUTXOs(storeId, cryptoCode));
        }

        public override async Task<OnChainWalletTransactionData> CreateOnChainTransaction(string storeId,
            string cryptoCode, CreateOnChainTransactionRequest request,
            CancellationToken token = default)
        {
            if (!request.ProceedWithBroadcast)
            {
                throw new ArgumentOutOfRangeException(nameof(request.ProceedWithBroadcast),
                    "Please use CreateOnChainTransactionButDoNotBroadcast when wanting to only create the transaction");
            }

            return GetFromActionResult<OnChainWalletTransactionData>(
                await _storeOnChainWalletsController.CreateOnChainTransaction(storeId, cryptoCode, request));
        }

        public override async Task<Transaction> CreateOnChainTransactionButDoNotBroadcast(string storeId,
            string cryptoCode,
            CreateOnChainTransactionRequest request, Network network, CancellationToken token = default)
        {
            if (request.ProceedWithBroadcast)
            {
                throw new ArgumentOutOfRangeException(nameof(request.ProceedWithBroadcast),
                    "Please use CreateOnChainTransaction when wanting to also broadcast the transaction");
            }

            return Transaction.Parse(
                GetFromActionResult<string>(
                    await _storeOnChainWalletsController.CreateOnChainTransaction(storeId, cryptoCode, request)),
                network);
        }

        public override async Task<IEnumerable<StoreData>> GetStores(CancellationToken token = default)
        {
            return GetFromActionResult(await _storesController.GetStores());
        }

        public override Task<StoreData> GetStore(string storeId, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<StoreData>(_storesController.GetStore(storeId)));
        }

        public override async Task RemoveStore(string storeId, CancellationToken token = default)
        {
            HandleActionResult(await _storesController.RemoveStore(storeId));
        }

        public override async Task<StoreData> CreateStore(CreateStoreRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<StoreData>(await _storesController.CreateStore(request));
        }

        public override async Task<StoreData> UpdateStore(string storeId, UpdateStoreRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<StoreData>(await _storesController.UpdateStore(storeId, request));
        }

        public override Task<IEnumerable<LNURLPayPaymentMethodData>>
            GetStoreLNURLPayPaymentMethods(string storeId, bool? enabled,
                CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult(
                _storeLnurlPayPaymentMethodsController.GetLNURLPayPaymentMethods(storeId, enabled)));
        }

        public override Task<LNURLPayPaymentMethodData> GetStoreLNURLPayPaymentMethod(
            string storeId, string cryptoCode, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<LNURLPayPaymentMethodData>(
                _storeLnurlPayPaymentMethodsController.GetLNURLPayPaymentMethod(storeId, cryptoCode)));
        }

        public override async Task RemoveStoreLNURLPayPaymentMethod(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            HandleActionResult(
                await _storeLnurlPayPaymentMethodsController.RemoveLNURLPayPaymentMethod(storeId,
                    cryptoCode));
        }

        public override async Task<LNURLPayPaymentMethodData> UpdateStoreLNURLPayPaymentMethod(
            string storeId, string cryptoCode,
            LNURLPayPaymentMethodData paymentMethod, CancellationToken token = default)
        {
            return GetFromActionResult<LNURLPayPaymentMethodData>(await
                _storeLnurlPayPaymentMethodsController.UpdateLNURLPayPaymentMethod(storeId, cryptoCode,
                    paymentMethod));
        }

        public override Task<IEnumerable<LightningNetworkPaymentMethodData>>
            GetStoreLightningNetworkPaymentMethods(string storeId, bool? enabled,
                CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult(
                _storeLightningNetworkPaymentMethodsController.GetLightningPaymentMethods(storeId, enabled)));
        }

        public override Task<LightningNetworkPaymentMethodData> GetStoreLightningNetworkPaymentMethod(
            string storeId, string cryptoCode, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult(
                _storeLightningNetworkPaymentMethodsController.GetLightningNetworkPaymentMethod(storeId, cryptoCode)));
        }

        public override async Task RemoveStoreLightningNetworkPaymentMethod(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            HandleActionResult(
                await _storeLightningNetworkPaymentMethodsController.RemoveLightningNetworkPaymentMethod(storeId,
                    cryptoCode));
        }

        public override async Task<LightningNetworkPaymentMethodData> UpdateStoreLightningNetworkPaymentMethod(
            string storeId, string cryptoCode,
            UpdateLightningNetworkPaymentMethodRequest paymentMethod, CancellationToken token = default)
        {
            return GetFromActionResult<LightningNetworkPaymentMethodData>(await
                _storeLightningNetworkPaymentMethodsController.UpdateLightningNetworkPaymentMethod(storeId, cryptoCode,
                    new UpdateLightningNetworkPaymentMethodRequest(paymentMethod.ConnectionString, paymentMethod.Enabled)));
        }

        public override async Task<IEnumerable<InvoiceData>> GetInvoices(string storeId, string[] orderId = null,
            InvoiceStatus[] status = null,
            DateTimeOffset? startDate = null,
            DateTimeOffset? endDate = null,
            string textSearch = null,
            bool includeArchived = false,
            int? skip = null,
            int? take = null,
            CancellationToken token = default

            )
        {
            return GetFromActionResult<IEnumerable<InvoiceData>>(
                await _greenFieldInvoiceController.GetInvoices(storeId, orderId,
                    status?.Select(invoiceStatus => invoiceStatus.ToString())?.ToArray(), startDate,
                    endDate, textSearch, includeArchived, skip, take));
        }

        public override async Task<InvoiceData> GetInvoice(string storeId, string invoiceId,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(await _greenFieldInvoiceController.GetInvoice(storeId, invoiceId));
        }

        public override async Task<InvoicePaymentMethodDataModel[]> GetInvoicePaymentMethods(string storeId,
            string invoiceId, CancellationToken token = default)
        {
            return GetFromActionResult<InvoicePaymentMethodDataModel[]>(
                await _greenFieldInvoiceController.GetInvoicePaymentMethods(storeId, invoiceId));
        }

        public override async Task ArchiveInvoice(string storeId, string invoiceId, CancellationToken token = default)
        {
            HandleActionResult(await _greenFieldInvoiceController.ArchiveInvoice(storeId, invoiceId));
        }

        public override async Task<InvoiceData> CreateInvoice(string storeId, CreateInvoiceRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(await _greenFieldInvoiceController.CreateInvoice(storeId, request));
        }

        public override async Task<InvoiceData> UpdateInvoice(string storeId, string invoiceId,
            UpdateInvoiceRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(
                await _greenFieldInvoiceController.UpdateInvoice(storeId, invoiceId, request));
        }

        public override async Task<InvoiceData> MarkInvoiceStatus(string storeId, string invoiceId,
            MarkInvoiceStatusRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(
                await _greenFieldInvoiceController.MarkInvoiceStatus(storeId, invoiceId, request));
        }

        public override async Task<InvoiceData> UnarchiveInvoice(string storeId, string invoiceId,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(
                await _greenFieldInvoiceController.UnarchiveInvoice(storeId, invoiceId));
        }

        public override Task<ServerInfoData> GetServerInfo(CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<ServerInfoData>(_greenFieldServerInfoController.ServerInfo()));
        }

        public override async Task ActivateInvoicePaymentMethod(string storeId, string invoiceId, string paymentMethod,
            CancellationToken token = default)
        {
            HandleActionResult(
                await _greenFieldInvoiceController.ActivateInvoicePaymentMethod(storeId, invoiceId, paymentMethod));
        }

        public override async Task<OnChainWalletFeeRateData> GetOnChainFeeRate(string storeId, string cryptoCode,
            int? blockTarget = null, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletFeeRateData>(
                await _storeOnChainWalletsController.GetOnChainFeeRate(storeId, cryptoCode, blockTarget));
        }

        public override async Task DeleteCurrentUser(CancellationToken token = default)
        {
            HandleActionResult(await _usersController.DeleteCurrentUser());
        }

        public override async Task DeleteUser(string userId, CancellationToken token = default)
        {
            HandleActionResult(await _usersController.DeleteUser(userId));
        }

        public override Task<Language[]> GetAvailableLanguages(CancellationToken token = default)
        {
            return Task.FromResult(_homeController.LanguageService.GetLanguages().Select(language => new Language(language.Code, language.DisplayName)).ToArray());
        }

        public override Task<PermissionMetadata[]> GetPermissionMetadata(CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<PermissionMetadata[]>(_homeController.Permissions()));
        }

        public override async Task<Dictionary<string, GenericPaymentMethodData>> GetStorePaymentMethods(string storeId, bool? enabled = null, CancellationToken token = default)
        {
            return GetFromActionResult(await _storePaymentMethodsController.GetStorePaymentMethods(storeId, enabled));
        }

        public override async Task<OnChainPaymentMethodDataWithSensitiveData> GenerateOnChainWallet(string storeId, string cryptoCode, GenerateOnChainWalletRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainPaymentMethodDataWithSensitiveData>(await _chainPaymentMethodsController.GenerateOnChainWallet(storeId, cryptoCode, new GenerateWalletRequest()
            {
                Passphrase = request.Passphrase,
                AccountNumber = request.AccountNumber,
                ExistingMnemonic = request.ExistingMnemonic?.ToString(),
                WordCount = request.WordCount,
                WordList = request.WordList,
                SavePrivateKeys = request.SavePrivateKeys,
                ScriptPubKeyType = request.ScriptPubKeyType,
                ImportKeysToRPC = request.ImportKeysToRPC
            }));
        }

        public override async Task SendEmail(string storeId, SendEmailRequest request, CancellationToken token = default)
        {
            HandleActionResult(await _greenfieldStoreEmailController.SendEmailFromStore(storeId, request));
        }
    }
}
