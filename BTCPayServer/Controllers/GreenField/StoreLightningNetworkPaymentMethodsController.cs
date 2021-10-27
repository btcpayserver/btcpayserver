#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class StoreLightningNetworkPaymentMethodsController : ControllerBase
    {
        private StoreData Store => HttpContext.GetStoreData();
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISettingsRepository _settingsRepository;

        public StoreLightningNetworkPaymentMethodsController(
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            IAuthorizationService authorizationService,
            ISettingsRepository settingsRepository)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _authorizationService = authorizationService;
            _settingsRepository = settingsRepository;
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
                        paymentMethod.PaymentId.ToStringNormalized(),
                        paymentMethod.DisableBOLT11PaymentOption
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
            if (!GetNetwork(cryptoCode, out BTCPayNetwork _))
            {
                return NotFound();
            }

            var method = GetExistingLightningLikePaymentMethod(_btcPayNetworkProvider, cryptoCode, Store);
            if (method is null)
            {
                return NotFound();
            }

            return Ok(method);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}")]
        public async Task<IActionResult> RemoveLightningNetworkPaymentMethod(
            string storeId,
            string cryptoCode)
        {
            if (!GetNetwork(cryptoCode, out BTCPayNetwork _))
            {
                return NotFound();
            }

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

            if (!GetNetwork(cryptoCode, out var network))
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(request.ConnectionString))
            {
                ModelState.AddModelError(nameof(LightningNetworkPaymentMethodData.ConnectionString),
                    "Missing connectionString");
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            LightningSupportedPaymentMethod? paymentMethod = null;
            if (!string.IsNullOrEmpty(request!.ConnectionString))
            {
                if (request.ConnectionString == LightningSupportedPaymentMethod.InternalNode)
                {
                    if (!await CanUseInternalLightning())
                    {
                        ModelState.AddModelError(nameof(request.ConnectionString),
                            $"You are not authorized to use the internal lightning node");
                        return this.CreateValidationError(ModelState);
                    }

                    paymentMethod = new Payments.Lightning.LightningSupportedPaymentMethod()
                    {
                        CryptoCode = paymentMethodId.CryptoCode
                    };
                    paymentMethod.SetInternalNode();
                }
                else
                {
                    if (!LightningConnectionString.TryParse(request.ConnectionString, false,
                        out var connectionString, out var error))
                    {
                        ModelState.AddModelError(nameof(request.ConnectionString), $"Invalid URL ({error})");
                        return this.CreateValidationError(ModelState);
                    }

                    if (connectionString.ConnectionType == LightningConnectionType.LndGRPC)
                    {
                        ModelState.AddModelError(nameof(request.ConnectionString),
                            $"BTCPay does not support gRPC connections");
                        return this.CreateValidationError(ModelState);
                    }

                    if (!await CanManageServer() && !connectionString.IsSafe())
                    {
                        ModelState.AddModelError(nameof(request.ConnectionString),
                            $"You do not have 'btcpay.server.canmodifyserversettings' rights, so the connection string should not contain 'cookiefilepath', 'macaroondirectorypath', 'macaroonfilepath', and should not point to a local ip or to a dns name ending with '.internal', '.local', '.lan' or '.'.");
                        return this.CreateValidationError(ModelState);
                    }

                    paymentMethod = new Payments.Lightning.LightningSupportedPaymentMethod()
                    {
                        CryptoCode = paymentMethodId.CryptoCode
                    };
                    paymentMethod.SetLightningUrl(connectionString);
                }
            }

            var store = Store;
            var storeBlob = store.GetStoreBlob();
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
                    paymentMethod.PaymentId.ToStringNormalized(), paymentMethod.DisableBOLT11PaymentOption);
        }

        private bool GetNetwork(string cryptoCode, [MaybeNullWhen(false)] out BTCPayNetwork network)
        {
            network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            network = network?.SupportLightning is true ? network : null;
            return network != null;
        }

        private async Task<bool> CanUseInternalLightning()
        {
            return (await _settingsRepository.GetPolicies()).AllowLightningInternalNodeForAll ||
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
