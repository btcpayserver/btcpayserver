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
using BTCPayServer.Security;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using InvoiceData = BTCPayServer.Client.Models.InvoiceData;
using Language = BTCPayServer.Client.Models.Language;
using LightningAddressData = BTCPayServer.Client.Models.LightningAddressData;
using NotificationData = BTCPayServer.Client.Models.NotificationData;
using PaymentRequestData = BTCPayServer.Client.Models.PaymentRequestData;
using PayoutData = BTCPayServer.Client.Models.PayoutData;
using PayoutProcessorData = BTCPayServer.Client.Models.PayoutProcessorData;
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
        private readonly UserManager<ApplicationUser> _userManager;

        private readonly IServiceProvider _serviceProvider;

        public BTCPayServerClientFactory(
            StoreRepository storeRepository,
            IOptionsMonitor<IdentityOptions> identityOptions,
            UserManager<ApplicationUser> userManager,
            IServiceProvider serviceProvider)
        {
            _storeRepository = storeRepository;
            _identityOptions = identityOptions;
            _userManager = userManager;
            _serviceProvider = serviceProvider;
        }

        public Task<BTCPayServerClient> Create(string userId, params string[] storeIds)
        {
            return Create(userId, storeIds, new DefaultHttpContext()
            {
                Request =
                {
                    Scheme = "https",
                    Host = new HostString("dummy.com"),
                    Path = new PathString(),
                    PathBase = new PathString(),
                }
            });
        }

        public async Task<BTCPayServerClient> Create(string userId, string[] storeIds, HttpContext context)
        {
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
                    new ClaimsPrincipal(new ClaimsIdentity(claims,
                        $"Local{GreenfieldConstants.AuthenticationType}WithUser"));
            }
            else
            {
                context.User =
                    new ClaimsPrincipal(new ClaimsIdentity(
                        new List<Claim>()
                        {
                            new(_identityOptions.CurrentValue.ClaimsIdentity.RoleClaimType, Roles.ServerAdmin)
                        },
                        $"Local{GreenfieldConstants.AuthenticationType}"));
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

            return ActivatorUtilities.CreateInstance<LocalBTCPayServerClient>(_serviceProvider,
                new LocalHttpContextAccessor() { HttpContext = context });

        }
    }


    public class LocalHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext HttpContext { get; set; }
    }

    public class LocalBTCPayServerClient : BTCPayServerClient
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IServiceProvider _serviceProvider;


        public LocalBTCPayServerClient(
            IHttpContextAccessor httpContextAccessor,
            IServiceProvider serviceProvider) : base(new Uri("https://dummy.local"), "", "")
        {
            _httpContextAccessor = httpContextAccessor;
            _serviceProvider = serviceProvider;
        }

        private T GetController<T>() where T : ControllerBase
        {
            var authoverride = new AuthorizationService(new GreenfieldAuthorizationHandler(_httpContextAccessor,
                _serviceProvider.GetService<UserManager<ApplicationUser>>(),
                _serviceProvider.GetService<StoreRepository>(),
                _serviceProvider.GetService<IPluginHookService>()));

            var controller = _serviceProvider.GetService<T>();
            controller.ControllerContext.HttpContext = _httpContextAccessor.HttpContext;
            var authInterface = typeof(IAuthorizationService);
            var type = controller.GetType();
            do
            {
                foreach (FieldInfo fieldInfo in type.GetFields(BindingFlags.FlattenHierarchy |
                                                               BindingFlags.Instance |
                                                               BindingFlags.NonPublic |
                                                               BindingFlags.Public |
                                                               BindingFlags.Static)
                             .Where(info =>
                                 authInterface == info.FieldType || authInterface.IsAssignableFrom(info.FieldType)))
                {
                    fieldInfo.SetValue(controller, authoverride);
                }

                type = type.BaseType;
            } while (type is not null);

            return controller;
        }

        class AuthorizationService : IAuthorizationService
        {
            private readonly GreenfieldAuthorizationHandler _greenfieldAuthorizationHandler;

            public AuthorizationService(GreenfieldAuthorizationHandler greenfieldAuthorizationHandler)
            {
                _greenfieldAuthorizationHandler = greenfieldAuthorizationHandler;
            }

            public async Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object resource,
                IEnumerable<IAuthorizationRequirement> requirements)
            {
                var withuser = user.Identity?.AuthenticationType ==
                               $"Local{GreenfieldConstants.AuthenticationType}WithUser";
                if (withuser)
                {
                    var newUser = new ClaimsPrincipal(new ClaimsIdentity(user.Claims,
                        $"{GreenfieldConstants.AuthenticationType}"));
                    var newContext = new AuthorizationHandlerContext(requirements, newUser, resource);
                    await _greenfieldAuthorizationHandler.HandleAsync(newContext);
                    if (newContext.HasSucceeded)
                    {
                        return AuthorizationResult.Success();
                    }

                    return AuthorizationResult.Failed();
                }

                var succeed = user.Identity.AuthenticationType == $"Local{GreenfieldConstants.AuthenticationType}";

                if (succeed)
                {
                    return AuthorizationResult.Success();
                }

                return AuthorizationResult.Failed();
            }

            public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object resource, string policyName)
            {
                return AuthorizeAsync(user, resource,
                    new List<IAuthorizationRequirement>(new[] { new PolicyRequirement(policyName) }));
            }
        }

        protected override HttpRequestMessage CreateHttpRequest(string path,
            Dictionary<string, object> queryPayload = null, HttpMethod method = null)
        {
            throw new NotSupportedException("This method is not supported by the LocalBTCPayServerClient.");
        }

        public override async Task<MarketTradeResponseData> MarketTradeCustodianAccountAsset(string storeId, string accountId,
            TradeRequestData request, CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<MarketTradeResponseData>(
                await GetController<GreenfieldCustodianAccountController>().MarketTradeCustodianAccountAsset(storeId, accountId, request, cancellationToken));
        }

        public override async Task<WithdrawalSimulationResponseData> SimulateCustodianAccountWithdrawal(string storeId, string accountId,
            WithdrawRequestData request, CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<WithdrawalSimulationResponseData>(
                await GetController<GreenfieldCustodianAccountController>().SimulateWithdrawal(storeId, accountId, request, cancellationToken));
        }

        public override async Task<WithdrawalResponseData> CreateCustodianAccountWithdrawal(string storeId, string accountId,
            WithdrawRequestData request, CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<WithdrawalResponseData>(
                await GetController<GreenfieldCustodianAccountController>().CreateWithdrawal(storeId, accountId, request, cancellationToken));
        }

        public override async Task<OnChainWalletObjectData[]> GetOnChainWalletObjects(string storeId, string cryptoCode,
            GetWalletObjectsRequest query = null,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletObjectData[]>(
                await GetController<GreenfieldStoreOnChainWalletsController>().GetOnChainWalletObjects(storeId, cryptoCode, query?.Type, query?.Ids, query?.IncludeNeighbourData));
        }

        public override async Task<OnChainWalletObjectData> GetOnChainWalletObject(string storeId, string cryptoCode, OnChainWalletObjectId objectId, bool? includeNeighbourData = null, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletObjectData>(
                await GetController<GreenfieldStoreOnChainWalletsController>().GetOnChainWalletObject(storeId, cryptoCode, objectId.Type, objectId.Id, includeNeighbourData));
        }
        public override async Task<OnChainWalletObjectData> AddOrUpdateOnChainWalletObject(string storeId, string cryptoCode, AddOnChainWalletObjectRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletObjectData>(
                await GetController<GreenfieldStoreOnChainWalletsController>().AddOrUpdateOnChainWalletObject(storeId, cryptoCode, request));
        }

        public override async Task RemoveOnChainWalletLinks(string storeId, string cryptoCode, OnChainWalletObjectId objectId, OnChainWalletObjectId link, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreOnChainWalletsController>().RemoveOnChainWalletLink(storeId, cryptoCode, objectId.Type, objectId.Id, link.Type, link.Id));
        }

        public override async Task RemoveOnChainWalletObject(string storeId, string cryptoCode, OnChainWalletObjectId objectId, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreOnChainWalletsController>().RemoveOnChainWalletObject(storeId, cryptoCode, objectId.Type, objectId.Id));
        }

        public override async Task AddOrUpdateOnChainWalletLink(string storeId, string cryptoCode, OnChainWalletObjectId objectId, AddOnChainWalletObjectLinkRequest request = null, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreOnChainWalletsController>().AddOrUpdateOnChainWalletLinks(storeId, cryptoCode, objectId.Type, objectId.Id, request));
        }

        public override async Task<StoreWebhookData> CreateWebhook(string storeId, CreateStoreWebhookRequest create,
            CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData>(
                await GetController<GreenfieldStoreWebhooksController>().CreateWebhook(storeId, create));
        }

        public override async Task<StoreWebhookData> GetWebhook(string storeId, string webhookId,
            CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData>(
                await GetController<GreenfieldStoreWebhooksController>().ListWebhooks(storeId, webhookId));
        }

        public override async Task<StoreWebhookData> UpdateWebhook(string storeId, string webhookId,
            UpdateStoreWebhookRequest update,
            CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData>(
                await GetController<GreenfieldStoreWebhooksController>().UpdateWebhook(storeId, webhookId, update));
        }

        public override async Task<bool> DeleteWebhook(string storeId, string webhookId,
            CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreWebhooksController>().DeleteWebhook(storeId, webhookId));
            return true;
        }

        public override async Task<StoreWebhookData[]> GetWebhooks(string storeId, CancellationToken token = default)
        {
            return GetFromActionResult<StoreWebhookData[]>(
                await GetController<GreenfieldStoreWebhooksController>().ListWebhooks(storeId, null));
        }

        public override async Task<WebhookDeliveryData[]> GetWebhookDeliveries(string storeId, string webhookId,
            CancellationToken token = default)
        {
            return GetFromActionResult<WebhookDeliveryData[]>(
                await GetController<GreenfieldStoreWebhooksController>().ListDeliveries(storeId, webhookId, null));
        }

        public override async Task<WebhookDeliveryData> GetWebhookDelivery(string storeId, string webhookId,
            string deliveryId, CancellationToken token = default)
        {
            return GetFromActionResult<WebhookDeliveryData>(
                await GetController<GreenfieldStoreWebhooksController>().ListDeliveries(storeId, webhookId, deliveryId));
        }

        public override async Task<string> RedeliverWebhook(string storeId, string webhookId, string deliveryId,
            CancellationToken token = default)
        {
            return GetFromActionResult<string>(
                await GetController<GreenfieldStoreWebhooksController>().RedeliverWebhook(storeId, webhookId, deliveryId));
        }

        public override async Task<WebhookEvent> GetWebhookDeliveryRequest(string storeId, string webhookId,
            string deliveryId, CancellationToken token = default)
        {
            return GetFromActionResult<WebhookEvent>(
                await GetController<GreenfieldStoreWebhooksController>().GetDeliveryRequest(storeId, webhookId, deliveryId));
        }

        public override async Task<PullPaymentData> CreatePullPayment(string storeId, CreatePullPaymentRequest request,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PullPaymentData>(
                await GetController<GreenfieldPullPaymentController>().CreatePullPayment(storeId, request));
        }

        public override async Task<PullPaymentData> GetPullPayment(string pullPaymentId,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PullPaymentData>(
                await GetController<GreenfieldPullPaymentController>().GetPullPayment(pullPaymentId));
        }

        public override async Task<PullPaymentData[]> GetPullPayments(string storeId, bool includeArchived = false,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PullPaymentData[]>(
                await GetController<GreenfieldPullPaymentController>().GetPullPayments(storeId, includeArchived));
        }

        public override async Task ArchivePullPayment(string storeId, string pullPaymentId,
            CancellationToken cancellationToken = default)
        {
            HandleActionResult(await GetController<GreenfieldPullPaymentController>().ArchivePullPayment(storeId, pullPaymentId));
        }

        public override async Task<PayoutData[]> GetPayouts(string pullPaymentId, bool includeCancelled = false,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData[]>(
                await GetController<GreenfieldPullPaymentController>().GetPayouts(pullPaymentId, includeCancelled));
        }

        public override async Task<PayoutData> CreatePayout(string pullPaymentId, CreatePayoutRequest payoutRequest,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData>(
                await GetController<GreenfieldPullPaymentController>().CreatePayout(pullPaymentId, payoutRequest, cancellationToken));
        }

        public override async Task CancelPayout(string storeId, string payoutId,
            CancellationToken cancellationToken = default)
        {
            HandleActionResult(await GetController<GreenfieldPullPaymentController>().CancelPayout(storeId, payoutId));
        }

        public override async Task<PayoutData> ApprovePayout(string storeId, string payoutId,
            ApprovePayoutRequest request, CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData>(
                await GetController<GreenfieldPullPaymentController>().ApprovePayout(storeId, payoutId, request, cancellationToken));
        }

        public override async Task<LightningNodeInformationData> GetLightningNodeInfo(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningNodeInformationData>(
                await GetController<GreenfieldStoreLightningNodeApiController>().GetInfo(cryptoCode, token));
        }

        public override async Task<LightningNodeBalanceData> GetLightningNodeBalance(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningNodeBalanceData>(
                await GetController<GreenfieldStoreLightningNodeApiController>().GetBalance(cryptoCode, token));
        }

        public override async Task ConnectToLightningNode(string storeId, string cryptoCode,
            ConnectToNodeRequest request, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreLightningNodeApiController>().ConnectToNode(cryptoCode, request, token));
        }

        public override async Task<IEnumerable<LightningChannelData>> GetLightningNodeChannels(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<LightningChannelData>>(
                await GetController<GreenfieldStoreLightningNodeApiController>().GetChannels(cryptoCode, token));
        }

        public override async Task OpenLightningChannel(string storeId, string cryptoCode,
            OpenLightningChannelRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreLightningNodeApiController>().OpenChannel(cryptoCode, request, token));
        }

        public override async Task<string> GetLightningDepositAddress(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<string>(
                await GetController<GreenfieldStoreLightningNodeApiController>().GetDepositAddress(cryptoCode, token));
        }

        public override async Task<LightningPaymentData> PayLightningInvoice(string storeId, string cryptoCode,
            PayLightningInvoiceRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<LightningPaymentData>(
                await GetController<GreenfieldStoreLightningNodeApiController>().PayInvoice(cryptoCode, request, token));
        }

        public override async Task<LightningInvoiceData> GetLightningInvoice(string storeId, string cryptoCode,
            string invoiceId, CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await GetController<GreenfieldStoreLightningNodeApiController>().GetInvoice(cryptoCode, invoiceId, token));
        }

        public override async Task<LightningInvoiceData[]> GetLightningInvoices(string storeId, string cryptoCode,
            bool? pendingOnly = null, long? offsetIndex = null, CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData[]>(
                await GetController<GreenfieldStoreLightningNodeApiController>().GetInvoices(cryptoCode, pendingOnly, offsetIndex, token));
        }

        public override async Task<LightningPaymentData[]> GetLightningPayments(string storeId, string cryptoCode,
            bool? includePending = null, long? offsetIndex = null, CancellationToken token = default)
        {
            return GetFromActionResult<LightningPaymentData[]>(
                await GetController<GreenfieldStoreLightningNodeApiController>().GetPayments(cryptoCode, includePending, offsetIndex, token));
        }

        public override async Task<LightningInvoiceData> CreateLightningInvoice(string storeId, string cryptoCode,
            CreateLightningInvoiceRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await GetController<GreenfieldStoreLightningNodeApiController>().CreateInvoice(cryptoCode, request, token));
        }

        public override async Task<LightningNodeInformationData> GetLightningNodeInfo(string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningNodeInformationData>(
                await GetController<GreenfieldInternalLightningNodeApiController>().GetInfo(cryptoCode));
        }

        public override async Task<LightningNodeBalanceData> GetLightningNodeBalance(string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningNodeBalanceData>(
                await GetController<GreenfieldInternalLightningNodeApiController>().GetBalance(cryptoCode));
        }

        public override async Task ConnectToLightningNode(string cryptoCode, ConnectToNodeRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldInternalLightningNodeApiController>().ConnectToNode(cryptoCode, request, token));
        }

        public override async Task<IEnumerable<LightningChannelData>> GetLightningNodeChannels(string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<LightningChannelData>>(
                await GetController<GreenfieldInternalLightningNodeApiController>().GetChannels(cryptoCode, token));
        }

        public override async Task OpenLightningChannel(string cryptoCode, OpenLightningChannelRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldInternalLightningNodeApiController>().OpenChannel(cryptoCode, request, token));
        }

        public override async Task<string> GetLightningDepositAddress(string cryptoCode,
            CancellationToken token = default)
        {
            return GetFromActionResult<string>(
                await GetController<GreenfieldInternalLightningNodeApiController>().GetDepositAddress(cryptoCode, token));
        }

        public override async Task<LightningPaymentData> PayLightningInvoice(string cryptoCode,
            PayLightningInvoiceRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<LightningPaymentData>(
                await GetController<GreenfieldInternalLightningNodeApiController>().PayInvoice(cryptoCode, request, token));
        }

        public override async Task<LightningInvoiceData> GetLightningInvoice(string cryptoCode, string invoiceId,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await GetController<GreenfieldInternalLightningNodeApiController>().GetInvoice(cryptoCode, invoiceId, token));
        }

        public override async Task<LightningInvoiceData[]> GetLightningInvoices(string cryptoCode,
            bool? pendingOnly = null, long? offsetIndex = null, CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData[]>(
                await GetController<GreenfieldInternalLightningNodeApiController>().GetInvoices(cryptoCode, pendingOnly, offsetIndex, token));
        }

        public override async Task<LightningPaymentData[]> GetLightningPayments(string cryptoCode,
            bool? includePending = null, long? offsetIndex = null, CancellationToken token = default)
        {
            return GetFromActionResult<LightningPaymentData[]>(
                await GetController<GreenfieldInternalLightningNodeApiController>().GetPayments(cryptoCode, includePending, offsetIndex, token));
        }

        public override async Task<LightningInvoiceData> CreateLightningInvoice(string cryptoCode,
            CreateLightningInvoiceRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningInvoiceData>(
                await GetController<GreenfieldInternalLightningNodeApiController>().CreateInvoice(cryptoCode, request, token));
        }

        private T GetFromActionResult<T>(IActionResult result)
        {
            HandleActionResult(result);
            return result switch
            {
                JsonResult jsonResult => (T)jsonResult.Value,
                OkObjectResult { Value: T res } => res,
                OkObjectResult { Value: JValue res } => res.Value<T>(),
                _ => default
            };
        }

        private void HandleActionResult(IActionResult result)
        {
            switch (result)
            {
                case UnprocessableEntityObjectResult { Value: List<GreenfieldValidationError> validationErrors }:
                    throw new GreenfieldValidationException(validationErrors.ToArray());
                case BadRequestObjectResult { Value: GreenfieldAPIError error }:
                    throw new GreenfieldAPIException(400, error);
                case ObjectResult { Value: GreenfieldAPIError error }:
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
            bool? enabled, CancellationToken token = default)
        {
            return Task.FromResult(
                GetFromActionResult(GetController<GreenfieldStoreOnChainPaymentMethodsController>().GetOnChainPaymentMethods(storeId, enabled)));
        }

        public override Task<OnChainPaymentMethodData> GetStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult(
                GetController<GreenfieldStoreOnChainPaymentMethodsController>().GetOnChainPaymentMethod(storeId, cryptoCode)));
        }

        public override async Task RemoveStoreOnChainPaymentMethod(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreOnChainPaymentMethodsController>().RemoveOnChainPaymentMethod(storeId, cryptoCode));
        }

        public override async Task<OnChainPaymentMethodData> UpdateStoreOnChainPaymentMethod(string storeId,
            string cryptoCode, UpdateOnChainPaymentMethodRequest paymentMethod, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainPaymentMethodData>(
                await GetController<GreenfieldStoreOnChainPaymentMethodsController>().UpdateOnChainPaymentMethod(storeId, cryptoCode,
                    new UpdateOnChainPaymentMethodRequest(
                        enabled: paymentMethod.Enabled,
                        label: paymentMethod.Label,
                        accountKeyPath: paymentMethod.AccountKeyPath,
                        derivationScheme: paymentMethod.DerivationScheme
                    )));
        }

        public override Task<OnChainPaymentMethodPreviewResultData> PreviewProposedStoreOnChainPaymentMethodAddresses(
            string storeId, string cryptoCode,
            UpdateOnChainPaymentMethodRequest paymentMethod, int offset = 0, int amount = 10,
            CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<OnChainPaymentMethodPreviewResultData>(
                GetController<GreenfieldStoreOnChainPaymentMethodsController>().GetProposedOnChainPaymentMethodPreview(storeId, cryptoCode,
                    paymentMethod, offset, amount)));
        }

        public override Task<OnChainPaymentMethodPreviewResultData> PreviewStoreOnChainPaymentMethodAddresses(
            string storeId, string cryptoCode, int offset = 0, int amount = 10, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<OnChainPaymentMethodPreviewResultData>(
                GetController<GreenfieldStoreOnChainPaymentMethodsController>().GetOnChainPaymentMethodPreview(storeId, cryptoCode, offset,
                    amount)));
        }

        public override Task<ApiHealthData> GetHealth(CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<ApiHealthData>(GetController<GreenfieldHealthController>().GetHealth()));
        }

        public override async Task<IEnumerable<PaymentRequestData>> GetPaymentRequests(string storeId,
            bool includeArchived = false, CancellationToken token = default)
        {
            return GetFromActionResult(await GetController<GreenfieldPaymentRequestsController>().GetPaymentRequests(storeId, includeArchived));
        }

        public override async Task<PaymentRequestData> GetPaymentRequest(string storeId, string paymentRequestId,
            CancellationToken token = default)
        {
            return GetFromActionResult<PaymentRequestData>(
                await GetController<GreenfieldPaymentRequestsController>().GetPaymentRequest(storeId, paymentRequestId));
        }

        public override async Task ArchivePaymentRequest(string storeId, string paymentRequestId,
            CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldPaymentRequestsController>().ArchivePaymentRequest(storeId, paymentRequestId));
        }

        public override async Task<InvoiceData> PayPaymentRequest(string storeId, string paymentRequestId, PayPaymentRequestRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(
                await GetController<GreenfieldPaymentRequestsController>().PayPaymentRequest(storeId, paymentRequestId, request, token));
        }

        public override async Task<PaymentRequestData> CreatePaymentRequest(string storeId,
            CreatePaymentRequestRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<PaymentRequestData>(
                await GetController<GreenfieldPaymentRequestsController>().CreatePaymentRequest(storeId, request));
        }

        public override async Task<PaymentRequestData> UpdatePaymentRequest(string storeId, string paymentRequestId,
            UpdatePaymentRequestRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<PaymentRequestData>(
                await GetController<GreenfieldPaymentRequestsController>().UpdatePaymentRequest(storeId, paymentRequestId, request));
        }

        public override async Task<ApiKeyData> GetCurrentAPIKeyInfo(CancellationToken token = default)
        {
            return GetFromActionResult<ApiKeyData>(await GetController<GreenfieldApiKeysController>().GetKey());
        }

        public override async Task<ApiKeyData> CreateAPIKey(CreateApiKeyRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<ApiKeyData>(await GetController<GreenfieldApiKeysController>().CreateAPIKey(request));
        }

        public override async Task RevokeCurrentAPIKeyInfo(CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldApiKeysController>().RevokeCurrentKey());
        }

        public override async Task RevokeAPIKey(string apikey, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldApiKeysController>().RevokeAPIKey(apikey));
        }

        public override async Task<IEnumerable<NotificationData>> GetNotifications(bool? seen = null,
            int? skip = null, int? take = null, CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<NotificationData>>(
                await GetController<GreenfieldNotificationsController>().GetNotifications(seen, skip, take));
        }

        public override async Task<NotificationData> GetNotification(string notificationId,
            CancellationToken token = default)
        {
            return GetFromActionResult<NotificationData>(
                await GetController<GreenfieldNotificationsController>().GetNotification(notificationId));
        }

        public override async Task<NotificationData> UpdateNotification(string notificationId, bool? seen,
            CancellationToken token = default)
        {
            return GetFromActionResult<NotificationData>(
                await GetController<GreenfieldNotificationsController>().UpdateNotification(notificationId,
                    new UpdateNotification() { Seen = seen }));
        }

        public override async Task RemoveNotification(string notificationId, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldNotificationsController>().DeleteNotification(notificationId));
        }

        public override async Task<ApplicationUserData> GetCurrentUser(CancellationToken token = default)
        {
            return GetFromActionResult(await GetController<GreenfieldUsersController>().GetCurrentUser());
        }

        public override async Task<ApplicationUserData> CreateUser(CreateApplicationUserRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<ApplicationUserData>(await GetController<GreenfieldUsersController>().CreateUser(request, token));
        }

        public override async Task<OnChainWalletOverviewData> ShowOnChainWalletOverview(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletOverviewData>(
                await GetController<GreenfieldStoreOnChainWalletsController>().ShowOnChainWalletOverview(storeId, cryptoCode));
        }

        public override async Task<OnChainWalletAddressData> GetOnChainWalletReceiveAddress(string storeId,
            string cryptoCode, bool forceGenerate = false,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletAddressData>(
                await GetController<GreenfieldStoreOnChainWalletsController>().GetOnChainWalletReceiveAddress(storeId, cryptoCode,
                    forceGenerate));
        }

        public override async Task UnReserveOnChainWalletReceiveAddress(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            HandleActionResult(
                await GetController<GreenfieldStoreOnChainWalletsController>().UnReserveOnChainWalletReceiveAddress(storeId, cryptoCode));
        }

        public override async Task<IEnumerable<OnChainWalletTransactionData>> ShowOnChainWalletTransactions(
            string storeId, string cryptoCode, TransactionStatus[] statusFilter = null, string labelFilter = null, int skip = 0,
            CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<OnChainWalletTransactionData>>(
                await GetController<GreenfieldStoreOnChainWalletsController>().ShowOnChainWalletTransactions(storeId, cryptoCode, statusFilter));
        }

        public override async Task<OnChainWalletTransactionData> GetOnChainWalletTransaction(string storeId,
            string cryptoCode, string transactionId, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletTransactionData>(
                await GetController<GreenfieldStoreOnChainWalletsController>().GetOnChainWalletTransaction(storeId, cryptoCode, transactionId));
        }

        public override async Task<IEnumerable<OnChainWalletUTXOData>> GetOnChainWalletUTXOs(string storeId,
            string cryptoCode, CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<OnChainWalletUTXOData>>(
                await GetController<GreenfieldStoreOnChainWalletsController>().GetOnChainWalletUTXOs(storeId, cryptoCode));
        }

        public override async Task<OnChainWalletTransactionData> CreateOnChainTransaction(string storeId,
            string cryptoCode, CreateOnChainTransactionRequest request, CancellationToken token = default)
        {
            if (!request.ProceedWithBroadcast)
            {
                throw new ArgumentOutOfRangeException(nameof(request.ProceedWithBroadcast),
                    "Please use CreateOnChainTransactionButDoNotBroadcast when wanting to only create the transaction");
            }

            return GetFromActionResult<OnChainWalletTransactionData>(
                await GetController<GreenfieldStoreOnChainWalletsController>().CreateOnChainTransaction(storeId, cryptoCode, request));
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
                    await GetController<GreenfieldStoreOnChainWalletsController>().CreateOnChainTransaction(storeId, cryptoCode, request)),
                network);
        }

        public override async Task<IEnumerable<StoreData>> GetStores(CancellationToken token = default)
        {
            return GetFromActionResult(await GetController<GreenfieldStoresController>().GetStores());
        }

        public override Task<StoreData> GetStore(string storeId, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<StoreData>(GetController<GreenfieldStoresController>().GetStore(storeId)));
        }

        public override async Task RemoveStore(string storeId, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoresController>().RemoveStore(storeId));
        }

        public override async Task<StoreData> CreateStore(CreateStoreRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<StoreData>(await GetController<GreenfieldStoresController>().CreateStore(request));
        }

        public override async Task<StoreData> UpdateStore(string storeId, UpdateStoreRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<StoreData>(await GetController<GreenfieldStoresController>().UpdateStore(storeId, request));
        }

        public override Task<IEnumerable<LNURLPayPaymentMethodData>>
            GetStoreLNURLPayPaymentMethods(string storeId, bool? enabled,
                CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult(
                GetController<GreenfieldStoreLNURLPayPaymentMethodsController>().GetLNURLPayPaymentMethods(storeId, enabled)));
        }

        public override Task<LNURLPayPaymentMethodData> GetStoreLNURLPayPaymentMethod(
            string storeId, string cryptoCode, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<LNURLPayPaymentMethodData>(
                GetController<GreenfieldStoreLNURLPayPaymentMethodsController>().GetLNURLPayPaymentMethod(storeId, cryptoCode)));
        }

        public override async Task RemoveStoreLNURLPayPaymentMethod(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            HandleActionResult(
                await GetController<GreenfieldStoreLNURLPayPaymentMethodsController>().RemoveLNURLPayPaymentMethod(storeId,
                    cryptoCode));
        }

        public override async Task<LNURLPayPaymentMethodData> UpdateStoreLNURLPayPaymentMethod(
            string storeId, string cryptoCode,
            LNURLPayPaymentMethodData paymentMethod, CancellationToken token = default)
        {
            return GetFromActionResult<LNURLPayPaymentMethodData>(await
                GetController<GreenfieldStoreLNURLPayPaymentMethodsController>().UpdateLNURLPayPaymentMethod(storeId, cryptoCode,
                    paymentMethod));
        }

        public override Task<IEnumerable<LightningNetworkPaymentMethodData>>
            GetStoreLightningNetworkPaymentMethods(string storeId, bool? enabled,
                CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult(
                GetController<GreenfieldStoreLightningNetworkPaymentMethodsController>().GetLightningPaymentMethods(storeId, enabled)));
        }

        public override Task<LightningNetworkPaymentMethodData> GetStoreLightningNetworkPaymentMethod(
            string storeId, string cryptoCode, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult(
                GetController<GreenfieldStoreLightningNetworkPaymentMethodsController>().GetLightningNetworkPaymentMethod(storeId, cryptoCode)));
        }

        public override async Task RemoveStoreLightningNetworkPaymentMethod(string storeId, string cryptoCode,
            CancellationToken token = default)
        {
            HandleActionResult(
                await GetController<GreenfieldStoreLightningNetworkPaymentMethodsController>().RemoveLightningNetworkPaymentMethod(storeId,
                    cryptoCode));
        }

        public override async Task<LightningNetworkPaymentMethodData> UpdateStoreLightningNetworkPaymentMethod(
            string storeId, string cryptoCode,
            UpdateLightningNetworkPaymentMethodRequest paymentMethod, CancellationToken token = default)
        {
            return GetFromActionResult<LightningNetworkPaymentMethodData>(await
                GetController<GreenfieldStoreLightningNetworkPaymentMethodsController>().UpdateLightningNetworkPaymentMethod(storeId, cryptoCode,
                    new UpdateLightningNetworkPaymentMethodRequest(paymentMethod.ConnectionString,
                        paymentMethod.Enabled)));
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
                await GetController<GreenfieldInvoiceController>().GetInvoices(storeId, orderId,
                    status?.Select(invoiceStatus => invoiceStatus.ToString())?.ToArray(), startDate,
                    endDate, textSearch, includeArchived, skip, take));
        }

        public override async Task<InvoiceData> GetInvoice(string storeId, string invoiceId,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(await GetController<GreenfieldInvoiceController>().GetInvoice(storeId, invoiceId));
        }

        public override async Task<InvoicePaymentMethodDataModel[]> GetInvoicePaymentMethods(string storeId,
            string invoiceId, CancellationToken token = default)
        {
            return GetFromActionResult<InvoicePaymentMethodDataModel[]>(
                await GetController<GreenfieldInvoiceController>().GetInvoicePaymentMethods(storeId, invoiceId));
        }

        public override async Task ArchiveInvoice(string storeId, string invoiceId, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldInvoiceController>().ArchiveInvoice(storeId, invoiceId));
        }

        public override async Task<InvoiceData> CreateInvoice(string storeId, CreateInvoiceRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(await GetController<GreenfieldInvoiceController>().CreateInvoice(storeId, request));
        }

        public override async Task<InvoiceData> UpdateInvoice(string storeId, string invoiceId,
            UpdateInvoiceRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(
                await GetController<GreenfieldInvoiceController>().UpdateInvoice(storeId, invoiceId, request));
        }

        public override async Task<InvoiceData> MarkInvoiceStatus(string storeId, string invoiceId,
            MarkInvoiceStatusRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(
                await GetController<GreenfieldInvoiceController>().MarkInvoiceStatus(storeId, invoiceId, request));
        }

        public override async Task<InvoiceData> UnarchiveInvoice(string storeId, string invoiceId,
            CancellationToken token = default)
        {
            return GetFromActionResult<InvoiceData>(
                await GetController<GreenfieldInvoiceController>().UnarchiveInvoice(storeId, invoiceId));
        }

        public override Task<ServerInfoData> GetServerInfo(CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<ServerInfoData>(GetController<GreenfieldServerInfoController>().ServerInfo()));
        }

        public override async Task ActivateInvoicePaymentMethod(string storeId, string invoiceId, string paymentMethod,
            CancellationToken token = default)
        {
            HandleActionResult(
                await GetController<GreenfieldInvoiceController>().ActivateInvoicePaymentMethod(storeId, invoiceId, paymentMethod));
        }

        public override async Task<OnChainWalletFeeRateData> GetOnChainFeeRate(string storeId, string cryptoCode,
            int? blockTarget = null, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletFeeRateData>(
                await GetController<GreenfieldStoreOnChainWalletsController>().GetOnChainFeeRate(storeId, cryptoCode, blockTarget));
        }

        public override async Task DeleteCurrentUser(CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldUsersController>().DeleteCurrentUser());
        }

        public override async Task DeleteUser(string userId, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldUsersController>().DeleteUser(userId));
        }

        public override Task<Language[]> GetAvailableLanguages(CancellationToken token = default)
        {
            return Task.FromResult(GetController<UIHomeController>().LanguageService.GetLanguages()
                .Select(language => new Language(language.Code, language.DisplayName)).ToArray());
        }

        public override Task<PermissionMetadata[]> GetPermissionMetadata(CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<PermissionMetadata[]>(GetController<UIHomeController>().Permissions()));
        }

        public override async Task<Dictionary<string, GenericPaymentMethodData>> GetStorePaymentMethods(string storeId,
            bool? enabled = null, CancellationToken token = default)
        {
            return GetFromActionResult(await GetController<GreenfieldStorePaymentMethodsController>().GetStorePaymentMethods(storeId, enabled));
        }

        public override async Task<OnChainPaymentMethodDataWithSensitiveData> GenerateOnChainWallet(string storeId,
            string cryptoCode, GenerateOnChainWalletRequest request,
            CancellationToken token = default)
        {
            return GetFromActionResult<OnChainPaymentMethodDataWithSensitiveData>(
                await GetController<GreenfieldStoreOnChainPaymentMethodsController>().GenerateOnChainWallet(storeId, cryptoCode,
                    new GenerateWalletRequest()
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

        public override async Task SendEmail(string storeId, SendEmailRequest request,
            CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreEmailController>().SendEmailFromStore(storeId, request));
        }

        public override Task<EmailSettingsData> GetStoreEmailSettings(string storeId, CancellationToken token = default)
        {
            return Task.FromResult(
                GetFromActionResult<EmailSettingsData>(GetController<GreenfieldStoreEmailController>().GetStoreEmailSettings()));
        }

        public override async Task<EmailSettingsData> UpdateStoreEmailSettings(string storeId,
            EmailSettingsData request, CancellationToken token = default)
        {
            return GetFromActionResult<EmailSettingsData>(
                await GetController<GreenfieldStoreEmailController>().UpdateStoreEmailSettings(storeId,
                    JObject.FromObject(request).ToObject<EmailSettings>()));
        }

        public override async Task<ApplicationUserData[]> GetUsers(CancellationToken token = default)
        {
            return GetFromActionResult(await GetController<GreenfieldUsersController>().GetUsers());
        }

        public override Task<IEnumerable<StoreUserData>> GetStoreUsers(string storeId,
            CancellationToken token = default)
        {
            return Task.FromResult(
                GetFromActionResult<IEnumerable<StoreUserData>>(GetController<GreenfieldStoreUsersController>().GetStoreUsers()));
        }

        public override async Task AddStoreUser(string storeId, StoreUserData request,
            CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreUsersController>().AddStoreUser(storeId, request));
        }

        public override async Task RemoveStoreUser(string storeId, string userId, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreUsersController>().RemoveStoreUser(storeId, userId));
        }

        public override async Task<ApplicationUserData> GetUserByIdOrEmail(string idOrEmail,
            CancellationToken token = default)
        {
            return GetFromActionResult<ApplicationUserData>(await GetController<GreenfieldUsersController>().GetUser(idOrEmail));
        }

        public override async Task<bool> LockUser(string idOrEmail, bool disabled, CancellationToken token = default)
        {
            return GetFromActionResult<bool>(
                await GetController<GreenfieldUsersController>().LockUser(idOrEmail,
                    new LockUserRequest { Locked = disabled }));
        }

        public override async Task<OnChainWalletTransactionData> PatchOnChainWalletTransaction(string storeId,
            string cryptoCode, string transactionId,
            PatchOnChainTransactionRequest request, bool force = false, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainWalletTransactionData>(
                await GetController<GreenfieldStoreOnChainWalletsController>().PatchOnChainWalletTransaction(storeId, cryptoCode, transactionId,
                    request, force));
        }

        public override async Task<LightningPaymentData> GetLightningPayment(string cryptoCode, string paymentHash,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningPaymentData>(
                await GetController<GreenfieldInternalLightningNodeApiController>().GetPayment(cryptoCode, paymentHash, token));
        }

        public override async Task<LightningPaymentData> GetLightningPayment(string storeId, string cryptoCode,
            string paymentHash, CancellationToken token = default)
        {
            return GetFromActionResult<LightningPaymentData>(
                await GetController<GreenfieldStoreLightningNodeApiController>().GetPayment(cryptoCode, paymentHash, token));
        }

        public override async Task<PayoutData> CreatePayout(string storeId,
            CreatePayoutThroughStoreRequest payoutRequest,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData>(
                await GetController<GreenfieldPullPaymentController>().CreatePayoutThroughStore(storeId, payoutRequest));
        }

        public override async Task<IEnumerable<PayoutProcessorData>> GetPayoutProcessors(string storeId,
            CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<PayoutProcessorData>>(
                await GetController<GreenfieldStorePayoutProcessorsController>().GetStorePayoutProcessors(storeId));
        }

        public override Task<IEnumerable<PayoutProcessorData>> GetPayoutProcessors(CancellationToken token = default)
        {
            return Task.FromResult(
                GetFromActionResult<IEnumerable<PayoutProcessorData>>(GetController<GreenfieldPayoutProcessorsController>()
                    .GetPayoutProcessors()));
        }

        public override async Task RemovePayoutProcessor(string storeId, string processor, string paymentMethod,
            CancellationToken token = default)
        {
            HandleActionResult(
                await GetController<GreenfieldStorePayoutProcessorsController>().RemoveStorePayoutProcessor(storeId, processor,
                    paymentMethod));
        }

        public override async Task<IEnumerable<OnChainAutomatedPayoutSettings>>
            GetStoreOnChainAutomatedPayoutProcessors(string storeId, string paymentMethod = null,
                CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<OnChainAutomatedPayoutSettings>>(
                await GetController<GreenfieldStoreAutomatedOnChainPayoutProcessorsController>()
                    .GetStoreOnChainAutomatedPayoutProcessors(storeId, paymentMethod));
        }

        public override async Task<IEnumerable<LightningAutomatedPayoutSettings>>
            GetStoreLightningAutomatedPayoutProcessors(string storeId, string paymentMethod = null,
                CancellationToken token = default)
        {
            return GetFromActionResult<IEnumerable<LightningAutomatedPayoutSettings>>(
                await GetController<GreenfieldStoreAutomatedLightningPayoutProcessorsController>()
                    .GetStoreLightningAutomatedPayoutProcessors(storeId, paymentMethod));
        }

        public override async Task<OnChainAutomatedPayoutSettings> UpdateStoreOnChainAutomatedPayoutProcessors(
            string storeId, string paymentMethod,
            OnChainAutomatedPayoutSettings request, CancellationToken token = default)
        {
            return GetFromActionResult<OnChainAutomatedPayoutSettings>(
                await GetController<GreenfieldStoreAutomatedOnChainPayoutProcessorsController>()
                    .UpdateStoreOnchainAutomatedPayoutProcessor(storeId, paymentMethod, request));
        }

        public override async Task<LightningAutomatedPayoutSettings> UpdateStoreLightningAutomatedPayoutProcessors(
            string storeId, string paymentMethod,
            LightningAutomatedPayoutSettings request, CancellationToken token = default)
        {
            return GetFromActionResult<LightningAutomatedPayoutSettings>(
                await GetController<GreenfieldStoreAutomatedLightningPayoutProcessorsController>()
                    .UpdateStoreLightningAutomatedPayoutProcessor(storeId, paymentMethod, request));
        }

        public override async Task<PayoutData[]> GetStorePayouts(string storeId, bool includeCancelled = false,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData[]>(
                await GetController<GreenfieldPullPaymentController>()
                    .GetStorePayouts(storeId, includeCancelled));
        }

        public override async Task<PointOfSaleAppData> CreatePointOfSaleApp(
            string storeId,
            CreatePointOfSaleAppRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<PointOfSaleAppData>(
                await GetController<GreenfieldAppsController>().CreatePointOfSaleApp(storeId, request));
        }

        public override async Task<PointOfSaleAppData> UpdatePointOfSaleApp(
            string appId,
            CreatePointOfSaleAppRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<PointOfSaleAppData>(
               await GetController<GreenfieldAppsController>().UpdatePointOfSaleApp(appId, request));
        }

        public override async Task<CrowdfundAppData> CreateCrowdfundApp(
            string storeId,
            CreateCrowdfundAppRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<CrowdfundAppData>(
                await GetController<GreenfieldAppsController>().CreateCrowdfundApp(storeId, request));
        }

        public override async Task<AppDataBase> GetApp(string appId, CancellationToken token = default)
        {
            return GetFromActionResult<AppDataBase>(
                await GetController<GreenfieldAppsController>().GetApp(appId));
        }

        public override async Task<AppDataBase[]> GetAllApps(string storeId, CancellationToken token = default)
        {
            return GetFromActionResult<AppDataBase[]>(
                await GetController<GreenfieldAppsController>().GetAllApps(storeId));
        }

        public override async Task<AppDataBase[]> GetAllApps(CancellationToken token = default)
        {
            return GetFromActionResult<AppDataBase[]>(
                await GetController<GreenfieldAppsController>().GetAllApps());
        }

        public override async Task DeleteApp(string appId, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldAppsController>().DeleteApp(appId));
        }

        public override Task<List<RateSource>> GetRateSources(CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult(GetController<GreenfieldStoreRateConfigurationController>().GetRateSources()));
        }

        public override Task<StoreRateConfiguration> GetStoreRateConfiguration(string storeId, CancellationToken token = default)
        {
            return Task.FromResult(GetFromActionResult<StoreRateConfiguration>(GetController<GreenfieldStoreRateConfigurationController>().GetStoreRateConfiguration()));
        }

        public override async Task<List<StoreRateResult>> GetStoreRates(string storeId,
            string[] currencyPair, CancellationToken token = default)
        {
            return GetFromActionResult<List<StoreRateResult>>(await GetController<GreenfieldStoreRatesController>().GetStoreRates(currencyPair));
        }

        public override async Task<List<StoreRateResult>> PreviewUpdateStoreRateConfiguration(string storeId,
            StoreRateConfiguration request,
            string[] currencyPair,
            CancellationToken token = default)
        {
            return GetFromActionResult<List<StoreRateResult>>(
                await GetController<GreenfieldStoreRateConfigurationController>().PreviewUpdateStoreRateConfiguration(request,
                    currencyPair));
        }

        public override async Task<StoreRateConfiguration> UpdateStoreRateConfiguration(string storeId, StoreRateConfiguration request, CancellationToken token = default)
        {
            return GetFromActionResult<StoreRateConfiguration>(await GetController<GreenfieldStoreRateConfigurationController>().UpdateStoreRateConfiguration(request));
        }

        public override async Task MarkPayoutPaid(string storeId, string payoutId, CancellationToken cancellationToken = default)
        {
            HandleActionResult(await GetController<GreenfieldPullPaymentController>().MarkPayoutPaid(storeId, payoutId, cancellationToken));
        }

        public override async Task MarkPayout(string storeId, string payoutId, MarkPayoutRequest request,
            CancellationToken cancellationToken = default)
        {
            HandleActionResult(await GetController<GreenfieldPullPaymentController>().MarkPayout(storeId, payoutId, request));
        }

        public override async Task<PayoutData> GetPullPaymentPayout(string pullPaymentId, string payoutId, CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData>(await GetController<GreenfieldPullPaymentController>().GetPayout(pullPaymentId, payoutId));
        }

        public override async Task<PullPaymentLNURL> GetPullPaymentLNURL(string pullPaymentId, CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PullPaymentLNURL>(await GetController<GreenfieldPullPaymentController>().GetPullPaymentLNURL(pullPaymentId));
        }

        public override async Task<PayoutData> GetStorePayout(string storeId, string payoutId,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PayoutData>(await GetController<GreenfieldPullPaymentController>().GetStorePayout(storeId, payoutId));
        }

        public override async Task<LightningAddressData[]> GetStoreLightningAddresses(string storeId,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningAddressData[]>(await GetController<GreenfieldStoreLightningAddressesController>().GetStoreLightningAddresses(storeId));
        }

        public override async Task<LightningAddressData> GetStoreLightningAddress(string storeId, string username, CancellationToken token = default)
        {
            return GetFromActionResult<LightningAddressData>(await GetController<GreenfieldStoreLightningAddressesController>().GetStoreLightningAddress(storeId, username));
        }

        public override async Task<LightningAddressData> AddOrUpdateStoreLightningAddress(string storeId, string username, LightningAddressData data,
            CancellationToken token = default)
        {
            return GetFromActionResult<LightningAddressData>(await GetController<GreenfieldStoreLightningAddressesController>().AddOrUpdateStoreLightningAddress(storeId, username, data));
        }

        public override async Task RemoveStoreLightningAddress(string storeId, string username, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldStoreLightningAddressesController>().RemoveStoreLightningAddress(storeId, username));
        }

        public override async Task<PointOfSaleAppData> GetPosApp(string appId,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<PointOfSaleAppData>(await GetController<GreenfieldAppsController>().GetPosApp(appId));
        }

        public override async Task<CrowdfundAppData> GetCrowdfundApp(string appId,
            CancellationToken cancellationToken = default)
        {
            return GetFromActionResult<CrowdfundAppData>(await GetController<GreenfieldAppsController>().GetCrowdfundApp(appId));
        }
        public override async Task<PullPaymentData> RefundInvoice(string storeId, string invoiceId, RefundInvoiceRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<PullPaymentData>(await GetController<GreenfieldInvoiceController>().RefundInvoice(storeId, invoiceId, request, token));
        }
        public override async Task RevokeAPIKey(string userId, string apikey, CancellationToken token = default)
        {
            HandleActionResult(await GetController<GreenfieldApiKeysController>().RevokeAPIKey(userId, apikey));
        }

        public override async Task<ApiKeyData> CreateAPIKey(string userId, CreateApiKeyRequest request, CancellationToken token = default)
        {
            return GetFromActionResult<ApiKeyData>(await GetController<GreenfieldApiKeysController>().CreateUserAPIKey(userId, request));
        }

        public override async Task<List<RoleData>> GetServerRoles(CancellationToken token = default)
        {
            return GetFromActionResult<List<RoleData>>(await GetController<GreenfieldServerRolesController>().GetServerRoles());
        }
        public override async Task<List<RoleData>> GetStoreRoles(string storeId, CancellationToken token = default)
        {
            return GetFromActionResult<List<RoleData>>(await GetController<GreenfieldStoreRolesController>().GetStoreRoles(storeId));
        }
    }
}
