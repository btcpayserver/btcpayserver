using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
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
        private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;
        private readonly IAuthorizationService _authorizationService;
        private readonly CssThemeManager _cssThemeManager;

        public StoreLightningNetworkPaymentMethodsController(
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            IOptions<LightningNetworkOptions> lightningNetworkOptions,
            IAuthorizationService authorizationService,
            CssThemeManager cssThemeManager)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _lightningNetworkOptions = lightningNetworkOptions;
            _authorizationService = authorizationService;
            _cssThemeManager = cssThemeManager;
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork")]
        public ActionResult<IEnumerable<LightningNetworkPaymentMethodData>> GetLightningPaymentMethods(
            [FromQuery] bool enabledOnly = false)
        {
            var blob = Store.GetStoreBlob();
            var excludedPaymentMethods = blob.GetExcludedPaymentMethods();
            return Ok(Store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .Where((method) => method.PaymentId.PaymentType == PaymentTypes.LightningLike)
                .OfType<LightningSupportedPaymentMethod>()
                .Select(paymentMethod =>
                    new LightningNetworkPaymentMethodData(paymentMethod.PaymentId.CryptoCode,
                        paymentMethod.GetLightningUrl().ToString(), !excludedPaymentMethods.Match(paymentMethod.PaymentId)))
                .Where((result) => !enabledOnly || result.Enabled)
                .ToList()
            );
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}")]
        public ActionResult<LightningNetworkPaymentMethodData> GetLightningNetworkPaymentMethod(string cryptoCode)
        {
            if (!GetNetwork(cryptoCode, out BTCPayNetwork _))
            {
                return NotFound();
            }

            var method = GetExistingLightningLikePaymentMethod(cryptoCode);
            if (method is null)
            {
                return NotFound();
            }
            return Ok(method);
        }
        
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/LightningNetwork/{cryptoCode}")]
        public async Task<IActionResult> RemoveLightningNetworkPaymentMethod(
            string cryptoCode,
            int offset = 0, int amount = 10)
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
        public async Task<IActionResult> UpdateLightningNetworkPaymentMethod(string cryptoCode,
            [FromBody] LightningNetworkPaymentMethodData paymentMethodData)
        {
            var paymentMethodId = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);

            if (!GetNetwork(cryptoCode, out var network))
            {
                return NotFound();
            }

            var internalLightning = await GetInternalLightningNode(network.CryptoCode);

            if (string.IsNullOrEmpty(paymentMethodData?.ConnectionString))
            {
                ModelState.AddModelError(nameof(LightningNetworkPaymentMethodData.ConnectionString),
                    "Missing connectionString");
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            LightningSupportedPaymentMethod paymentMethod = null;
            if (!string.IsNullOrEmpty(paymentMethodData.ConnectionString))
            {
                if (!LightningConnectionString.TryParse(paymentMethodData.ConnectionString, false,
                    out var connectionString, out var error))
                {
                    ModelState.AddModelError(nameof(paymentMethodData.ConnectionString), $"Invalid URL ({error})");
                    return this.CreateValidationError(ModelState);
                }

                if (connectionString.ConnectionType == LightningConnectionType.LndGRPC)
                {
                    ModelState.AddModelError(nameof(paymentMethodData.ConnectionString),
                        $"BTCPay does not support gRPC connections");
                    return this.CreateValidationError(ModelState);
                }

                bool isInternalNode = connectionString.IsInternalNode(internalLightning);

                if (connectionString.BaseUri.Scheme == "http")
                {
                    if (!isInternalNode && !connectionString.AllowInsecure)
                    {
                        ModelState.AddModelError(nameof(paymentMethodData.ConnectionString), "The url must be HTTPS");
                        return this.CreateValidationError(ModelState);
                    }
                }

                if (connectionString.MacaroonFilePath != null)
                {
                    if (!await CanUseInternalLightning())
                    {
                        ModelState.AddModelError(nameof(paymentMethodData.ConnectionString),
                            "You are not authorized to use macaroonfilepath");
                        return this.CreateValidationError(ModelState);
                    }

                    if (!System.IO.File.Exists(connectionString.MacaroonFilePath))
                    {
                        ModelState.AddModelError(nameof(paymentMethodData.ConnectionString),
                            "The macaroonfilepath file does not exist");
                        return this.CreateValidationError(ModelState);
                    }

                    if (!System.IO.Path.IsPathRooted(connectionString.MacaroonFilePath))
                    {
                        ModelState.AddModelError(nameof(paymentMethodData.ConnectionString),
                            "The macaroonfilepath should be fully rooted");
                        return this.CreateValidationError(ModelState);
                    }
                }

                if (isInternalNode && !await CanUseInternalLightning())
                {
                    ModelState.AddModelError(nameof(paymentMethodData.ConnectionString), "Unauthorized url");
                    return this.CreateValidationError(ModelState);
                }

                paymentMethod = new Payments.Lightning.LightningSupportedPaymentMethod()
                {
                    CryptoCode = paymentMethodId.CryptoCode
                };
                paymentMethod.SetLightningUrl(connectionString);
            }

            var store = Store;
            var storeBlob = store.GetStoreBlob();
            store.SetSupportedPaymentMethod(paymentMethodId, paymentMethod);
            storeBlob.SetExcluded(paymentMethodId, !paymentMethodData.Enabled);
            store.SetStoreBlob(storeBlob);
            await _storeRepository.UpdateStore(store);
            return Ok(GetExistingLightningLikePaymentMethod(cryptoCode, store));
        }

        private LightningNetworkPaymentMethodData GetExistingLightningLikePaymentMethod(string cryptoCode, StoreData store = null)
        {
            store ??= Store;
            var storeBlob = store.GetStoreBlob();
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
            var paymentMethod = store
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<LightningSupportedPaymentMethod>()
                .FirstOrDefault(method => method.PaymentId == id);

            var excluded = storeBlob.IsExcluded(id);
            return paymentMethod == null
                ? null
                : new LightningNetworkPaymentMethodData(paymentMethod.PaymentId.CryptoCode,
                    paymentMethod.GetLightningUrl().ToString(), !excluded);
        }

        private bool GetNetwork(string cryptoCode, out BTCPayNetwork network)
        {
            network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            network = network?.SupportLightning is true ? network : null;
            return network != null;
        }
        
        private async Task<LightningConnectionString> GetInternalLightningNode(string cryptoCode)
        {
            if (_lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue(cryptoCode, out var connectionString))
            {
                return await CanUseInternalLightning() ? connectionString : null;
            }
            return null;
        }
        
        private async Task<bool> CanUseInternalLightning()
        {
            return _cssThemeManager.AllowLightningInternalNodeForAll ||
                (await _authorizationService.AuthorizeAsync(User, null,
                    new PolicyRequirement(Policies.CanUseInternalLightningNode))).Succeeded;
        }
    }
}
