using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Security.GreenField;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NBitcoin;
using InvoiceData = BTCPayServer.Client.Models.InvoiceData;
using NotificationData = BTCPayServer.Client.Models.NotificationData;
using PaymentRequestData = BTCPayServer.Client.Models.PaymentRequestData;
using PayoutData = BTCPayServer.Client.Models.PayoutData;
using PullPaymentData = BTCPayServer.Client.Models.PullPaymentData;
using StoreData = BTCPayServer.Client.Models.StoreData;
using StoreWebhookData = BTCPayServer.Client.Models.StoreWebhookData;
using WebhookDeliveryData = BTCPayServer.Client.Models.WebhookDeliveryData;

namespace BTCPayServer.Controllers.GreenField
{
    public class BTCPayServerClientFactory : IBTCPayServerClientFactory
    {
        private readonly StoreRepository _storeRepository;
        private readonly IOptionsMonitor<IdentityOptions> _identityOptions;
        private readonly StoreOnChainPaymentMethodsController _chainPaymentMethodsController;
        private readonly StoreOnChainWalletsController _storeOnChainWalletsController;
        private readonly StoreLightningNetworkPaymentMethodsController _storeLightningNetworkPaymentMethodsController;
        private readonly HealthController _healthController;
        private readonly GreenFieldPaymentRequestsController _paymentRequestController;
        private readonly ApiKeysController _apiKeysController;
        private readonly NotificationsController _notificationsController;
        private readonly UsersController _usersController;
        private readonly GreenFieldStoresController _storesController;
        private readonly InternalLightningNodeApiController _internalLightningNodeApiController;
        private readonly StoreLightningNodeApiController _storeLightningNodeApiController;
        private readonly GreenFieldInvoiceController _greenFieldInvoiceController;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly GreenFieldServerInfoController _greenFieldServerInfoController;
        private readonly StoreWebhooksController _storeWebhooksController;
        private readonly GreenfieldPullPaymentController _greenfieldPullPaymentController;

        public BTCPayServerClientFactory(StoreRepository storeRepository,
            IOptionsMonitor<IdentityOptions> identityOptions,
            StoreOnChainPaymentMethodsController chainPaymentMethodsController,
            StoreOnChainWalletsController storeOnChainWalletsController,
            StoreLightningNetworkPaymentMethodsController storeLightningNetworkPaymentMethodsController,
            HealthController healthController,
            GreenFieldPaymentRequestsController paymentRequestController,
            ApiKeysController apiKeysController,
            NotificationsController notificationsController,
            UsersController usersController,
            GreenFieldStoresController storesController,
            InternalLightningNodeApiController internalLightningNodeApiController,
            StoreLightningNodeApiController storeLightningNodeApiController,
            GreenFieldInvoiceController greenFieldInvoiceController,
            UserManager<ApplicationUser> userManager,
            GreenFieldServerInfoController greenFieldServerInfoController,
            StoreWebhooksController storeWebhooksController,
            GreenfieldPullPaymentController greenfieldPullPaymentController)
        {
            _storeRepository = storeRepository;
            _identityOptions = identityOptions;
            _chainPaymentMethodsController = chainPaymentMethodsController;
            _storeOnChainWalletsController = storeOnChainWalletsController;
            _storeLightningNetworkPaymentMethodsController = storeLightningNetworkPaymentMethodsController;
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
                    new Claim(GreenFieldConstants.ClaimTypes.Permission,
                        Permission.Create(Policies.Unrestricted).ToString())
                };
                claims.AddRange((await _userManager.GetRolesAsync(user)).Select(s =>
                    new Claim(_identityOptions.CurrentValue.ClaimsIdentity.RoleClaimType, s)));
                context.User =
                    new ClaimsPrincipal(new ClaimsIdentity(claims, GreenFieldConstants.AuthenticationType));
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
                _greenFieldInvoiceController,
                _greenFieldServerInfoController,
                _storeWebhooksController,
                _greenfieldPullPaymentController,
                new HttpContextAccessor() {HttpContext = context}
            );
        }
    }

    public class LocalBTCPayServerClient : BTCPayServerClient
    {
        private readonly StoreOnChainPaymentMethodsController _chainPaymentMethodsController;
        private readonly StoreOnChainWalletsController _storeOnChainWalletsController;
        private readonly HealthController _healthController;
        private readonly GreenFieldPaymentRequestsController _paymentRequestController;
        private readonly ApiKeysController _apiKeysController;
        private readonly NotificationsController _notificationsController;
        private readonly UsersController _usersController;
        private readonly GreenFieldStoresController _storesController;
        private readonly StoreLightningNodeApiController _storeLightningNodeApiController;
        private readonly InternalLightningNodeApiController _lightningNodeApiController;
        private readonly StoreLightningNetworkPaymentMethodsController _storeLightningNetworkPaymentMethodsController;
        private readonly GreenFieldInvoiceController _greenFieldInvoiceController;
        private readonly GreenFieldServerInfoController _greenFieldServerInfoController;
        private readonly StoreWebhooksController _storeWebhooksController;
        private readonly GreenfieldPullPaymentController _greenfieldPullPaymentController;

        public LocalBTCPayServerClient(StoreOnChainPaymentMethodsController chainPaymentMethodsController,
            StoreOnChainWalletsController storeOnChainWalletsController,
            HealthController healthController,
            GreenFieldPaymentRequestsController paymentRequestController,
            ApiKeysController apiKeysController,
            NotificationsController notificationsController,
            UsersController usersController,
            GreenFieldStoresController storesController,
            StoreLightningNodeApiController storeLightningNodeApiController,
            InternalLightningNodeApiController lightningNodeApiController,
            StoreLightningNetworkPaymentMethodsController storeLightningNetworkPaymentMethodsController,
            GreenFieldInvoiceController greenFieldInvoiceController,
            GreenFieldServerInfoController greenFieldServerInfoController,
            StoreWebhooksController storeWebhooksController,
            GreenfieldPullPaymentController greenfieldPullPaymentController,
            IHttpContextAccessor httpContextAccessor) : base(new Uri("http://dummy.com"), "", "")
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
            _greenFieldInvoiceController = greenFieldInvoiceController;
            _greenFieldServerInfoController = greenFieldServerInfoController;
            _storeWebhooksController = storeWebhooksController;
            _greenfieldPullPaymentController = greenfieldPullPaymentController;

            var controllers = new[]
            {
                chainPaymentMethodsController, storeOnChainWalletsController, healthController,
                paymentRequestController, apiKeysController, notificationsController, usersController,
                storeLightningNetworkPaymentMethodsController, greenFieldInvoiceController,
                greenFieldServerInfoController, greenfieldPullPaymentController, storesController,lightningNodeApiController,
                storeLightningNodeApiController as ControllerBase,
            };
            foreach (var controller in controllers)
            {
                controller.ControllerContext.HttpContext = httpContextAccessor.HttpContext;
            }
        }

        protected override HttpRequestMessage CreateHttpRequest(string path, Dictionary<string, object> queryPayload = null, HttpMethod method = null)
        {
            throw new NotSupportedException("This method is not supported by the LocalBTCPayServerClient.");
        }

        public override async Task<StoreWebhookData> CreateWebhook(string storeId, CreateStoreWebhookRequest create, CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData>(
                await _storeWebhooksController.CreateWebhook(storeId, create));
        }

        public override async Task<StoreWebhookData> GetWebhook(string storeId, string webhookId, CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData>(
                await _storeWebhooksController.ListWebhooks(storeId, webhookId));
        }

        public override async Task<StoreWebhookData> UpdateWebhook(string storeId, string webhookId, UpdateStoreWebhookRequest update,
            CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData>(
                await _storeWebhooksController.UpdateWebhook(storeId, webhookId, update));
        }

        public override async Task<bool> DeleteWebhook(string storeId, string webhookId, CancellationToken token = default)
        {
            HandleActionResult(await _storeWebhooksController.DeleteWebhook(storeId, webhookId));
            return true;
        }

        public override async Task<StoreWebhookData[]> GetWebhooks(string storeId, CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData[]>(
                await _storeWebhooksController.ListWebhooks(storeId, null));
        }

        public override async Task<WebhookDeliveryData[]> GetWebhookDeliveries(string storeId, string webhookId, CancellationToken token = default)
        {
            return GetFromActionResult<WebhookDeliveryData[]>(
                await _storeWebhooksController.ListDeliveries(storeId, webhookId, null));
        }

        public override async Task<WebhookDeliveryData> GetWebhookDelivery(string storeId, string webhookId, string deliveryId, CancellationToken token = default)
        {
            return GetFromActionResult<WebhookDeliveryData>(
                await _storeWebhooksController.ListDeliveries(storeId, webhookId, deliveryId));
        }

        public override async Task<string> RedeliverWebhook(string storeId, string webhookId, string deliveryId, CancellationToken token = default)
        {
            return GetFromActionResult<string>(
                await _storeWebhooksController.RedeliverWebhook(storeId, webhookId, deliveryId));
        }

        public override async Task<WebhookEvent> GetWebhookDeliveryRequest(string storeId, string webhookId, string deliveryId, CancellationToken token = default)
        {
            return GetFromActionResult<WebhookEvent>(
                await _storeWebhooksController.GetDeliveryRequest(storeId, webhookId, deliveryId));
        }

        public override async Task<PullPaymentData> CreatePullPayment(string storeId, CreatePullPaymentRequest request, CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PullPaymentData>(
                await _greenfieldPullPaymentController.CreatePullPayment(storeId, request));
        }

        public override async Task<PullPaymentData> GetPullPayment(string pullPaymentId, CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PullPaymentData>(
                await _greenfieldPullPaymentController.GetPullPayment(pullPaymentId));
        }

        public override async Task<PullPaymentData[]> GetPullPayments(string storeId, bool includeArchived = false, CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PullPaymentData[]>(
                await _greenfieldPullPaymentController.GetPullPayments(storeId,includeArchived ));
        }

        public override async Task ArchivePullPayment(string storeId, string pullPaymentId, CancellationToken cancellationToken = default)
        {
            HandleActionResult(await _greenfieldPullPaymentController.ArchivePullPayment(storeId, pullPaymentId));
        }

        public override async Task<PayoutData[]> GetPayouts(string pullPaymentId, bool includeCancelled = false, CancellationToken cancellationToken = default)
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

        public override async Task CancelPayout(string storeId, string payoutId, CancellationToken cancellationToken = default)
        {
            HandleActionResult(await _greenfieldPullPaymentController.CancelPayout(storeId, payoutId));
        }

        public override async Task<PayoutData> ApprovePayout(string storeId, string payoutId, ApprovePayoutRequest request,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData>(
                await _greenfieldPullPaymentController.ApprovePayout(storeId, payoutId, request, cancellationToken));
        }

        public override async Task<LightningNodeInformationData> GetLightningNodeInfo(string storeId, string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<LightningNodeInformationData>(
                await _storeLightningNodeApiController.GetInfo(cryptoCode));
        }

        public override async Task ConnectToLightningNode(string storeId, string cryptoCode, ConnectToNodeRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await _storeLightningNodeApiController.ConnectToNode(cryptoCode, request));
        }

        public override async Task<IEnumerable<LightningChannelData>> GetLightningNodeChannels(string storeId, string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<LightningChannelData>>(
                await _storeLightningNodeApiController.GetChannels(cryptoCode));
        }

        public override async Task OpenLightningChannel(string storeId, string cryptoCode, OpenLightningChannelRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await _storeLightningNodeApiController.OpenChannel(cryptoCode, request));
        }

        public override async Task<string> GetLightningDepositAddress(string storeId, string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<string>(
                await _storeLightningNodeApiController.GetDepositAddress(cryptoCode));
        }

        public override async Task PayLightningInvoice(string storeId, string cryptoCode, PayLightningInvoiceRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await _storeLightningNodeApiController.PayInvoice(cryptoCode, request));
        }

        public override async Task<LightningInvoiceData> GetLightningInvoice(string storeId, string cryptoCode, string invoiceId, CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await _storeLightningNodeApiController.GetInvoice(cryptoCode, invoiceId));
        }

        public override async Task<LightningInvoiceData> CreateLightningInvoice(string storeId, string cryptoCode, CreateLightningInvoiceRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await _storeLightningNodeApiController.CreateInvoice(cryptoCode, request));
        }

        public override async Task<LightningNodeInformationData> GetLightningNodeInfo(string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<LightningNodeInformationData>(
                await _lightningNodeApiController.GetInfo(cryptoCode));
        }

        public override async Task ConnectToLightningNode(string cryptoCode, ConnectToNodeRequest request, CancellationToken token = default)
        {
            HandleActionResult(await _lightningNodeApiController.ConnectToNode(cryptoCode, request));
        }

        public override async Task<IEnumerable<LightningChannelData>> GetLightningNodeChannels(string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<LightningChannelData>>(
                await _lightningNodeApiController.GetChannels(cryptoCode));
        }

        public override async Task OpenLightningChannel(string cryptoCode, OpenLightningChannelRequest request, CancellationToken token = default)
        {
            HandleActionResult(await _lightningNodeApiController.OpenChannel(cryptoCode, request));
        }

        public override async Task<string> GetLightningDepositAddress(string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<string>(
                await _lightningNodeApiController.GetDepositAddress(cryptoCode));
        }

        public override async Task PayLightningInvoice(string cryptoCode, PayLightningInvoiceRequest request, CancellationToken token = default)
        {
            HandleActionResult(await _lightningNodeApiController.PayInvoice(cryptoCode, request));
        }

        public override async Task<LightningInvoiceData> GetLightningInvoice(string cryptoCode, string invoiceId, CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await _lightningNodeApiController.GetInvoice(cryptoCode, invoiceId));
        }

        public override async Task<LightningInvoiceData> CreateLightningInvoice(string cryptoCode, CreateLightningInvoiceRequest request,
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
                case OkObjectResult {Value: T res}:
                    return res;
                default:
                    return default;
            }
        }

        private void HandleActionResult(IActionResult result)
        {
            switch (result)
            {
                case UnprocessableEntityObjectResult {Value: List<GreenfieldValidationError> validationErrors}:
                    throw new GreenFieldValidationException(validationErrors.ToArray());
                case BadRequestObjectResult {Value: GreenfieldAPIError error}:
                    throw new GreenFieldAPIException(error);
                case NotFoundResult _:
                    throw new GreenFieldAPIException(new GreenfieldAPIError("not-found", ""));
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
        public override async Task<IEnumerable<OnChainPaymentMethodData>> GetStoreOnChainPaymentMethods(string storeId,
            CancellationToken token = default)
        {
            return GetFromActionResult(_chainPaymentMethodsController.GetOnChainPaymentMethods(storeId));
        }

        public override async Task<OnChainPaymentMethodData> GetStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult(
                await _chainPaymentMethodsController.GetOnChainPaymentMethod(storeId, cryptoCode));
        }

        public override async Task RemoveStoreOnChainPaymentMethod(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            HandleActionResult(await _chainPaymentMethodsController.RemoveOnChainPaymentMethod(storeId, cryptoCode));
        }

        public override async Task<OnChainPaymentMethodData> UpdateStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, OnChainPaymentMethodData paymentMethod,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainPaymentMethodData>(
                await _chainPaymentMethodsController.UpdateOnChainPaymentMethod(storeId, cryptoCode, paymentMethod));
        }

        public override Task<OnChainPaymentMethodPreviewResultData> PreviewProposedStoreOnChainPaymentMethodAddresses(
            string storeId, string cryptoCode,
            OnChainPaymentMethodData paymentMethod, int offset = 0, int amount = 10, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<OnChainPaymentMethodPreviewResultData>(
                _chainPaymentMethodsController.GetProposedOnChainPaymentMethodPreview(storeId, cryptoCode,
                    paymentMethod, offset, amount)));
        }

        public override async Task<OnChainPaymentMethodPreviewResultData> PreviewStoreOnChainPaymentMethodAddresses(
            string storeId, string cryptoCode, int offset = 0, int amount = 10,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainPaymentMethodPreviewResultData>(
                await _chainPaymentMethodsController.GetOnChainPaymentMethodPreview(storeId, cryptoCode, offset,
                    amount));
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
            return GetFromActionResult(await _paymentRequestController.GetPaymentRequest(storeId, paymentRequestId));
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
            return GetFromActionResult(await _apiKeysController.GetKey());
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
            CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<NotificationData>>(
                await _notificationsController.GetNotifications(seen));
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
                    new UpdateNotification() {Seen = seen}));
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

        public override async Task<StoreData> GetStore(string storeId, CancellationToken token = default)
        {
            return GetFromActionResult(await _storesController.GetStore(storeId));
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

        public override async Task<IEnumerable<LightningNetworkPaymentMethodData>>
            GetStoreLightningNetworkPaymentMethods(string storeId, bool enabledOnly = false,CancellationToken token = default)
        {
            return GetFromActionResult(
                _storeLightningNetworkPaymentMethodsController.GetLightningPaymentMethods(storeId, enabledOnly));
        }

        public override async Task<LightningNetworkPaymentMethodData> GetStoreLightningNetworkPaymentMethod(
            string storeId, string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult(
                _storeLightningNetworkPaymentMethodsController.GetLightningNetworkPaymentMethod(storeId, cryptoCode));
        }

        public override async Task RemoveStoreLightningNetworkPaymentMethod(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            HandleActionResult(await _storeLightningNetworkPaymentMethodsController.RemoveLightningNetworkPaymentMethod(storeId, cryptoCode));
        }

        public override async Task<LightningNetworkPaymentMethodData> UpdateStoreLightningNetworkPaymentMethod(
            string storeId, string cryptoCode,
            LightningNetworkPaymentMethodData paymentMethod, CancellationToken token = default)
        {
            return GetFromActionResult<LightningNetworkPaymentMethodData>(await 
                _storeLightningNetworkPaymentMethodsController.UpdateLightningNetworkPaymentMethod(storeId, cryptoCode, paymentMethod) );
        }

        public override async Task<IEnumerable<InvoiceData>> GetInvoices(string storeId, bool includeArchived = false,
            CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<InvoiceData>>(await  _greenFieldInvoiceController.GetInvoices(storeId, includeArchived) );
        }

        public override async Task<InvoiceData> GetInvoice(string storeId, string invoiceId,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(await  _greenFieldInvoiceController.GetInvoice(storeId, invoiceId) );
        }

        public override async Task<InvoicePaymentMethodDataModel[]> GetInvoicePaymentMethods(string storeId,
            string invoiceId, CancellationToken token = default)
        {
            return GetFromActionResult<InvoicePaymentMethodDataModel[]>(await  _greenFieldInvoiceController.GetInvoicePaymentMethods(storeId, invoiceId) );
        }

        public override async Task ArchiveInvoice(string storeId, string invoiceId, CancellationToken token = default)
        {
            HandleActionResult(await _greenFieldInvoiceController.ArchiveInvoice(storeId, invoiceId));
        }

        public override async Task<InvoiceData> CreateInvoice(string storeId, CreateInvoiceRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(await  _greenFieldInvoiceController.CreateInvoice(storeId, request) );
        }

        public override async Task<InvoiceData> UpdateInvoice(string storeId, string invoiceId,
            UpdateInvoiceRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(await  _greenFieldInvoiceController.UpdateInvoice(storeId, invoiceId, request) );
        }

        public override async Task<InvoiceData> MarkInvoiceStatus(string storeId, string invoiceId,
            MarkInvoiceStatusRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(await  _greenFieldInvoiceController.MarkInvoiceStatus(storeId, invoiceId, request) );
        }

        public override async Task<InvoiceData> UnarchiveInvoice(string storeId, string invoiceId,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(await  _greenFieldInvoiceController.UnarchiveInvoice(storeId, invoiceId) );
        }

        public override async Task<ServerInfoData> GetServerInfo(CancellationToken token = default)
        {
            return GetFromActionResult<ServerInfoData>(await  _greenFieldServerInfoController.ServerInfo() );
        }

        public override async Task ActivateInvoicePaymentMethod(string storeId, string invoiceId, string paymentMethod,
            CancellationToken token = default)
        {
            HandleActionResult(await _greenFieldInvoiceController.ActivateInvoicePaymentMethod(storeId, invoiceId, paymentMethod));
        }

        public override async Task<OnChainWalletFeeRateData> GetOnChainFeeRate(string storeId, string cryptoCode,
            int? blockTarget = null, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletFeeRateData>(await  _storeOnChainWalletsController.GetOnChainFeeRate(storeId,cryptoCode, blockTarget) );
        }
    }
}
