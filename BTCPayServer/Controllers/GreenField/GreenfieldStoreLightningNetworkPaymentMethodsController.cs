#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreLightningNetworkPaymentMethodsController : ControllerBase
    {
        private StoreData Store => HttpContext.GetStoreData();

        public PoliciesSettings PoliciesSettings { get; }

        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly IAuthorizationService _authorizationService;
        private readonly LightningClientFactoryService _lightningClientFactoryService;

        public GreenfieldStoreLightningNetworkPaymentMethodsController(
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            IAuthorizationService authorizationService,
            LightningClientFactoryService lightningClientFactoryService,
            PoliciesSettings policiesSettings)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _authorizationService = authorizationService;
            _lightningClientFactoryService = lightningClientFactoryService;
            PoliciesSettings = policiesSettings;
        }

        public static IEnumerable<LightningNetworkPaymentMethodData> GetLightningPaymentMethods(StoreData store,
            BTCPayNetworkProvider networkProvider, bool? enabled)
        {
            var blob = store.GetStoreBlob();
            var excludedPaymentMethods = blob.GetExcludedPaymentMethods();

            return store.GetSupportedPaymentMethods(networkProvider)
                .Where((method) => method.PaymentId.PaymentType == PaymentTypes.LightningLike)
                .OfType<LightningSupportedPaymentMethod>()
                .Select(paymentMethod =>
                    new LightningNetworkPaymentMethodData(
                        paymentMethod.PaymentId.CryptoCode,
                        paymentMethod.GetExternalLightningUrl()?.ToString() ??
                        paymentMethod.GetDisplayableConnectionString(),
                        !excludedPaymentMethods.Match(paymentMethod.PaymentId),
                        paymentMethod.PaymentId.ToStringNormalized()
                    )
                )
                .Where((result) => enabled is null || enabled == result.Enabled)
                .ToList();
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork")]
        public ActionResult<IEnumerable<LightningNetworkPaymentMethodData>> GetLightningPaymentMethods(
            string storeId,
            [FromQuery] bool? enabled)
        {
            return Ok(GetLightningPaymentMethods(Store, _btcPayNetworkProvider, enabled));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}")]
        public ActionResult<LightningNetworkPaymentMethodData> GetLightningNetworkPaymentMethod(string storeId, string cryptoCode)
        {
            AssertSupportLightning(cryptoCode);

            var method = GetExistingLightningLikePaymentMethod(_btcPayNetworkProvider, cryptoCode, Store);
            if (method is null)
            {
                throw ErrorPaymentMethodNotConfigured();
            }

            return Ok(method);
        }

        protected JsonHttpException ErrorPaymentMethodNotConfigured()
        {
            return new JsonHttpException(this.CreateAPIError(404, "paymentmethod-not-configured", "The lightning payment method is not set up"));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}")]
        public async Task<IActionResult> RemoveLightningNetworkPaymentMethod(
            string storeId,
            string cryptoCode)
        {
            AssertSupportLightning(cryptoCode);

            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var store = Store;
            store.SetSupportedPaymentMethod(id, null);
            await _storeRepository.UpdateStore(store);
            return Ok();
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}")]
        public async Task<IActionResult> UpdateLightningNetworkPaymentMethod(string storeId, string cryptoCode,
            [FromBody] UpdateLightningNetworkPaymentMethodRequest request)
        {
            var paymentMethodId = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            AssertSupportLightning(cryptoCode);

            if (string.IsNullOrEmpty(request.ConnectionString))
            {
                ModelState.AddModelError(nameof(LightningNetworkPaymentMethodData.ConnectionString),
                    "Missing connectionString");
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            LightningSupportedPaymentMethod? paymentMethod = null;
            var store = Store;
            var storeBlob = store.GetStoreBlob();
            var existing = GetExistingLightningLikePaymentMethod(_btcPayNetworkProvider, cryptoCode, store);
            if (existing == null || existing.ConnectionString != request.ConnectionString)
            {
                if (request.ConnectionString == LightningSupportedPaymentMethod.InternalNode)
                {
                    if (!await CanUseInternalLightning())
                    {
                        return this.CreateAPIPermissionError(Policies.CanUseInternalLightningNode, $"You are not authorized to use the internal lightning node. Either add '{Policies.CanUseInternalLightningNode}' to an API Key, or allow non-admin users to use the internal lightning node in the server settings.");
                    }

                    paymentMethod = new Payments.Lightning.LightningSupportedPaymentMethod()
                    {
                        CryptoCode = paymentMethodId.CryptoCode
                    };
                    paymentMethod.SetInternalNode();
                }
                else
                {
                    ILightningClient? lightningClient;
                    try
                    {
                        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
                        lightningClient = _lightningClientFactoryService.Create(request.ConnectionString, network);
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError(nameof(request.ConnectionString), $"Invalid URL ({e.Message})");
                        return this.CreateValidationError(ModelState);
                    }

                    // if (connectionString.ConnectionType == LightningConnectionType.LndGRPC)
                    // {
                    //     ModelState.AddModelError(nameof(request.ConnectionString),
                    //         $"BTCPay does not support gRPC connections");
                    //     return this.CreateValidationError(ModelState);
                    // }

                    if (!await CanManageServer() && !lightningClient.IsSafe())
                    {
                        ModelState.AddModelError(nameof(request.ConnectionString),
                            $"You do not have 'btcpay.server.canmodifyserversettings' rights, so the connection string should not contain 'cookiefilepath', 'macaroondirectorypath', 'macaroonfilepath', and should not point to a local ip or to a dns name ending with '.internal', '.local', '.lan' or '.'.");
                        return this.CreateValidationError(ModelState);
                    }

                    paymentMethod = new Payments.Lightning.LightningSupportedPaymentMethod()
                    {
                        CryptoCode = paymentMethodId.CryptoCode
                    };
                    paymentMethod.SetLightningUrl(lightningClient);
                }
            }
            store.SetSupportedPaymentMethod(paymentMethodId, paymentMethod);
            storeBlob.SetExcluded(paymentMethodId, !request.Enabled);
            store.SetStoreBlob(storeBlob);
            await _storeRepository.UpdateStore(store);
            return Ok(GetExistingLightningLikePaymentMethod(_btcPayNetworkProvider, cryptoCode, store));
        }

        public static LightningNetworkPaymentMethodData? GetExistingLightningLikePaymentMethod(BTCPayNetworkProvider btcPayNetworkProvider, string cryptoCode,
            StoreData store)
        {

            var storeBlob = store.GetStoreBlob();
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var paymentMethod = store
                .GetSupportedPaymentMethods(btcPayNetworkProvider)
                .OfType<LightningSupportedPaymentMethod>()
                .FirstOrDefault(method => method.PaymentId == id);

            var excluded = storeBlob.IsExcluded(id);
            return paymentMethod is null
                ? null
                : new LightningNetworkPaymentMethodData(paymentMethod.PaymentId.CryptoCode,
                    paymentMethod.GetDisplayableConnectionString(), !excluded,
                    paymentMethod.PaymentId.ToStringNormalized());
        }

        private BTCPayNetwork AssertSupportLightning(string cryptoCode)
        {
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null)
                throw new JsonHttpException(this.CreateAPIError(404, "unknown-cryptocode", "This crypto code isn't set up in this BTCPay Server instance"));
            if (!(network.SupportLightning is true))
                throw new JsonHttpException(this.CreateAPIError(404, "unknown-cryptocode", "This crypto code doesn't support lightning"));
            return network;
        }

        private async Task<bool> CanUseInternalLightning()
        {
            return PoliciesSettings.AllowLightningInternalNodeForAll ||
                   (await _authorizationService.AuthorizeAsync(User, null,
                       new PolicyRequirement(Policies.CanUseInternalLightningNode))).Succeeded;
        }

        private async Task<bool> CanManageServer()
        {
            return
                (await _authorizationService.AuthorizeAsync(User, null,
                    new PolicyRequirement(Policies.CanModifyServerSettings))).Succeeded;
        }
    }
}
