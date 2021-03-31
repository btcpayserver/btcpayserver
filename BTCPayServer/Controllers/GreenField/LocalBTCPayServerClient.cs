using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Controllers.GreenField
{
    public class LocalBTCPayServerClient: BTCPayServerClient
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
        private readonly IHttpContextAccessor _httpContextAccessor;

        public  LocalBTCPayServerClient(
            StoreOnChainPaymentMethodsController chainPaymentMethodsController,
            StoreOnChainWalletsController storeOnChainWalletsController,
            HealthController healthController, 
            GreenFieldPaymentRequestsController paymentRequestController,
            ApiKeysController apiKeysController,
            NotificationsController notificationsController, 
            UsersController usersController,
            GreenFieldStoresController storesController,
            StoreLightningNodeApiController storeLightningNodeApiController,
            IHttpContextAccessor httpContextAccessor) : base(new Uri(""), "", "")
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
            _httpContextAccessor = httpContextAccessor;
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
            return GetFromActionResult<T>(result.Result);
        }

        public override async Task<IEnumerable<OnChainPaymentMethodData>> GetStoreOnChainPaymentMethods(string storeId, CancellationToken token = default)
        {
            return GetFromActionResult(await _chainPaymentMethodsController.GetOnChainPaymentMethods(storeId));
        }

        public override async Task<OnChainPaymentMethodData> GetStoreOnChainPaymentMethod(string storeId, string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult(await _chainPaymentMethodsController.GetOnChainPaymentMethod(storeId, cryptoCode));
        }

        public override async Task RemoveStoreOnChainPaymentMethod(string storeId, string cryptoCode, CancellationToken token = default)
        {
           HandleActionResult( await _chainPaymentMethodsController.RemoveOnChainPaymentMethod(storeId, cryptoCode));
        }

        public override async Task<OnChainPaymentMethodData> UpdateStoreOnChainPaymentMethod(string storeId, string cryptoCode, OnChainPaymentMethodData paymentMethod,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainPaymentMethodData>(await _chainPaymentMethodsController.UpdateOnChainPaymentMethod(storeId, cryptoCode, paymentMethod));
        }

        public override Task<OnChainPaymentMethodPreviewResultData> PreviewProposedStoreOnChainPaymentMethodAddresses(string storeId, string cryptoCode,
            OnChainPaymentMethodData paymentMethod, int offset = 0, int amount = 10, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<OnChainPaymentMethodPreviewResultData>(_chainPaymentMethodsController.GetProposedOnChainPaymentMethodPreview(storeId, cryptoCode, paymentMethod, offset, amount)));
        }

        public override async Task<OnChainPaymentMethodPreviewResultData> PreviewStoreOnChainPaymentMethodAddresses(string storeId, string cryptoCode, int offset = 0, int amount = 10,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainPaymentMethodPreviewResultData>(await _chainPaymentMethodsController.GetOnChainPaymentMethodPreview(storeId, cryptoCode,  offset, amount));
        }

        public override Task<ApiHealthData> GetHealth(CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<ApiHealthData>(_healthController.GetHealth()));
        }

        public override async Task<IEnumerable<PaymentRequestData>> GetPaymentRequests(string storeId, bool includeArchived = false, CancellationToken token = default)
        {
            return GetFromActionResult(await _paymentRequestController.GetPaymentRequests(storeId, includeArchived));
        }

        public override async Task<PaymentRequestData> GetPaymentRequest(string storeId, string paymentRequestId, CancellationToken token = default)
        {
            return GetFromActionResult(await _paymentRequestController.GetPaymentRequest(storeId, paymentRequestId));
        }

        public override async Task ArchivePaymentRequest(string storeId, string paymentRequestId, CancellationToken token = default)
        {
            HandleActionResult( await _paymentRequestController.ArchivePaymentRequest(storeId, paymentRequestId));
        }

        public override async Task<PaymentRequestData> CreatePaymentRequest(string storeId, CreatePaymentRequestRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<PaymentRequestData>(await _paymentRequestController.CreatePaymentRequest(storeId, request));
        }

        public override async Task<PaymentRequestData> UpdatePaymentRequest(string storeId, string paymentRequestId, UpdatePaymentRequestRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<PaymentRequestData>(await _paymentRequestController.UpdatePaymentRequest(storeId, paymentRequestId, request));
        }

        public override async Task<ApiKeyData> GetCurrentAPIKeyInfo(CancellationToken token = default)
        {
            return GetFromActionResult(await _apiKeysController.GetKey());
        }

        public override async Task<ApiKeyData> CreateAPIKey(CreateApiKeyRequest request, CancellationToken token = default)
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

        public override async Task<IEnumerable<NotificationData>> GetNotifications(bool? seen = null, CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<NotificationData>>(await _notificationsController.GetNotifications(seen));
        }

        public override async Task<NotificationData> GetNotification(string notificationId, CancellationToken token = default)
        {
            return GetFromActionResult<NotificationData>(await _notificationsController.GetNotification(notificationId));
        }

        public override async Task<NotificationData> UpdateNotification(string notificationId, bool? seen, CancellationToken token = default)
        {
            return GetFromActionResult<NotificationData>(await _notificationsController.UpdateNotification(notificationId, new UpdateNotification()
            {
                Seen = seen
            }));
        }

        public override async Task RemoveNotification(string notificationId, CancellationToken token = default)
        {
            HandleActionResult(await _notificationsController.DeleteNotification(notificationId));
        }

        public override async Task<ApplicationUserData> GetCurrentUser(CancellationToken token = default)
        {
            return GetFromActionResult(await _usersController.GetCurrentUser());
        }

        public override async Task<ApplicationUserData> CreateUser(CreateApplicationUserRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<ApplicationUserData>(await _usersController.CreateUser(request, token));
        }

        public override async Task<OnChainWalletOverviewData> ShowOnChainWalletOverview(string storeId, string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletOverviewData>(await _storeOnChainWalletsController.ShowOnChainWalletOverview(storeId, cryptoCode));
        }

        public override async Task<OnChainWalletAddressData> GetOnChainWalletReceiveAddress(string storeId, string cryptoCode, bool forceGenerate = false,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletAddressData>(await _storeOnChainWalletsController.GetOnChainWalletReceiveAddress(storeId, cryptoCode, forceGenerate));
        }

        public override async Task UnReserveOnChainWalletReceiveAddress(string storeId, string cryptoCode, CancellationToken token = default)
        {
            HandleActionResult(await _storeOnChainWalletsController.UnReserveOnChainWalletReceiveAddress(storeId, cryptoCode));
        }

        public override async Task<IEnumerable<OnChainWalletTransactionData>> ShowOnChainWalletTransactions(string storeId, string cryptoCode, TransactionStatus[] statusFilter = null,
            CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<OnChainWalletTransactionData>>(await _storeOnChainWalletsController.ShowOnChainWalletTransactions(storeId, cryptoCode, statusFilter));
        }

        public override async Task<OnChainWalletTransactionData> GetOnChainWalletTransaction(string storeId, string cryptoCode, string transactionId,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletTransactionData>(await _storeOnChainWalletsController.GetOnChainWalletTransaction(storeId, cryptoCode, transactionId));
        }

        public override async Task<IEnumerable<OnChainWalletUTXOData>> GetOnChainWalletUTXOs(string storeId, string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<OnChainWalletUTXOData>>(await _storeOnChainWalletsController.GetOnChainWalletUTXOs(storeId, cryptoCode));
        }

        public override async Task<OnChainWalletTransactionData> CreateOnChainTransaction(string storeId, string cryptoCode, CreateOnChainTransactionRequest request,
            CancellationToken token = default)
        {
            if (!request.ProceedWithBroadcast)
            {
                throw new ArgumentOutOfRangeException(nameof(request.ProceedWithBroadcast),
                    "Please use CreateOnChainTransactionButDoNotBroadcast when wanting to only create the transaction");
            }
            return GetFromActionResult<OnChainWalletTransactionData>(await _storeOnChainWalletsController.CreateOnChainTransaction(storeId, cryptoCode, request));
        }

        public override async Task<Transaction> CreateOnChainTransactionButDoNotBroadcast(string storeId, string cryptoCode,
            CreateOnChainTransactionRequest request, Network network, CancellationToken token = default)
        {
            if (request.ProceedWithBroadcast)
            {
                throw new ArgumentOutOfRangeException(nameof(request.ProceedWithBroadcast),
                    "Please use CreateOnChainTransaction when wanting to also broadcast the transaction");
            }
            return Transaction.Parse( GetFromActionResult<string>(await _storeOnChainWalletsController.CreateOnChainTransaction(storeId, cryptoCode, request)), network);
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

        public override async Task<StoreData> UpdateStore(string storeId, UpdateStoreRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<StoreData>(await _storesController.UpdateStore(storeId, request));
        }

        public override async Task<IEnumerable<LightningNetworkPaymentMethodData>> GetStoreLightningNetworkPaymentMethods(string storeId, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<LightningNetworkPaymentMethodData> GetStoreLightningNetworkPaymentMethod(string storeId, string cryptoCode, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task RemoveStoreLightningNetworkPaymentMethod(string storeId, string cryptoCode, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<LightningNetworkPaymentMethodData> UpdateStoreLightningNetworkPaymentMethod(string storeId, string cryptoCode,
            LightningNetworkPaymentMethodData paymentMethod, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<LightningNetworkPaymentMethodData> UpdateStoreLightningNetworkPaymentMethodToInternalNode(string storeId, string cryptoCode,
            LightningNetworkPaymentMethodData paymentMethod, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<IEnumerable<InvoiceData>> GetInvoices(string storeId, bool includeArchived = false, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<InvoiceData> GetInvoice(string storeId, string invoiceId, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<InvoicePaymentMethodDataModel[]> GetInvoicePaymentMethods(string storeId, string invoiceId, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task ArchiveInvoice(string storeId, string invoiceId, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<InvoiceData> CreateInvoice(string storeId, CreateInvoiceRequest request, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<InvoiceData> UpdateInvoice(string storeId, string invoiceId, UpdateInvoiceRequest request, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<InvoiceData> MarkInvoiceStatus(string storeId, string invoiceId, MarkInvoiceStatusRequest request,
            CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<InvoiceData> UnarchiveInvoice(string storeId, string invoiceId, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public override async Task<ServerInfoData> GetServerInfo(CancellationToken token = default)
        {
            throw new NotSupportedException();
        }
    }
}
