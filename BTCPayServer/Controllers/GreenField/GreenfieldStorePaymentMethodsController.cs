#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using BTCPayServer.Abstractions.Extensions;
using StoreData = BTCPayServer.Data.StoreData;
using BTCPayServer.ModelBinders;
using BTCPayServer.Payments;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStorePaymentMethodsController : ControllerBase
    {
        private StoreData Store => HttpContext.GetStoreData();

        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly StoreRepository _storeRepository;
        private readonly IAuthorizationService _authorizationService;

        public GreenfieldStorePaymentMethodsController(
            PaymentMethodHandlerDictionary handlers,
            StoreRepository storeRepository,
            IAuthorizationService authorizationService)
        {
            _handlers = handlers;
            _storeRepository = storeRepository;
            _authorizationService = authorizationService;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}")]
        public async Task<IActionResult> GetStorePaymentMethod(
            string storeId,
            [ModelBinder(typeof(PaymentMethodIdModelBinder))]
            PaymentMethodId paymentMethodId,
            [FromQuery] bool? includeConfig)
        {
            var result = await GetStorePaymentMethods(storeId, onlyEnabled: false, includeConfig);
            if (result is OkObjectResult { Value: GenericPaymentMethodData[] methods })
            {
                var m = methods.FirstOrDefault(m => m.PaymentMethodId == paymentMethodId.ToString());
                return m is { } ? Ok(m) : PaymentMethodNotFound();
            }
            return result;
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}")]
        public async Task<IActionResult> RemoveStorePaymentMethod(
            string storeId,
            [ModelBinder(typeof(PaymentMethodIdModelBinder))]
            PaymentMethodId paymentMethodId)
        {
            AssertHasHandler(paymentMethodId);
            if (Store.GetPaymentMethodConfig(paymentMethodId) is null)
                return Ok();
            Store.SetPaymentMethodConfig(paymentMethodId, null);
            await _storeRepository.UpdateStore(Store);
            return Ok();
        }


        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}")]
        public async Task<IActionResult> UpdateStorePaymentMethod(
            string storeId,
            [ModelBinder(typeof(PaymentMethodIdModelBinder))]
            PaymentMethodId paymentMethodId,
            [FromBody] UpdatePaymentMethodRequest? request = null)
        {
            if (request is null)
            {
                ModelState.AddModelError(nameof(request), "Missing body");
                return this.CreateValidationError(ModelState);
            }
            var handler = AssertHasHandler(paymentMethodId);
            if (request?.Config is { } config)
            {
                try
                {
                    var ctx = new PaymentMethodConfigValidationContext(_authorizationService, ModelState, config, User, Store.GetPaymentMethodConfig(paymentMethodId));
                    await handler.ValidatePaymentMethodConfig(ctx);
                    config = ctx.Config;
                    if (ctx.MissingPermission is not null)
                    {
                        return this.CreateAPIPermissionError(ctx.MissingPermission.Permission, ctx.MissingPermission.Message);
                    }
                    if (!ModelState.IsValid)
                        return this.CreateValidationError(ModelState);
                    if (ctx.StripUnknownProperties)
                        config = JToken.FromObject(handler.ParsePaymentMethodConfig(config), handler.Serializer);
                }
                catch
                {
                    ModelState.AddModelError(nameof(config), "Invalid configuration");
                    return this.CreateValidationError(ModelState);
                }
                Store.SetPaymentMethodConfig(paymentMethodId, config);
            }
            if (request?.Enabled is { } enabled)
            {
                var storeBlob = Store.GetStoreBlob();
                storeBlob.SetExcluded(paymentMethodId, !enabled);
                Store.SetStoreBlob(storeBlob);
            }
            await _storeRepository.UpdateStore(Store);
            return await GetStorePaymentMethod(storeId, paymentMethodId, request?.Config is not null);
        }

        private IPaymentMethodHandler AssertHasHandler(PaymentMethodId paymentMethodId)
        {
            if (!_handlers.TryGetValue(paymentMethodId, out var handler))
                throw new JsonHttpException(PaymentMethodNotFound());
            return handler;
        }

        private IActionResult PaymentMethodNotFound()
        {
            return this.CreateAPIError(404, "paymentmethod-not-found", "The payment method is not found");
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods")]
        public async Task<IActionResult> GetStorePaymentMethods(
            string storeId,
            [FromQuery] bool? onlyEnabled, [FromQuery] bool? includeConfig)
        {
            var storeBlob = Store.GetStoreBlob();
            var excludedPaymentMethods = storeBlob.GetExcludedPaymentMethods();

            if (includeConfig is true)
            {
                if (!await _authorizationService.CanModifyStore(User))
                    return this.CreateAPIPermissionError(Policies.CanModifyStoreSettings);
            }

            return Ok(Store.GetPaymentMethodConfigs(_handlers, onlyEnabled is true)
                .Select(
                    method => new GenericPaymentMethodData()
                    {
                        PaymentMethodId = method.Key.ToString(),
                        Enabled = onlyEnabled.GetValueOrDefault(!excludedPaymentMethods.Match(method.Key)),
                        Config = includeConfig is true ? JToken.FromObject(method.Value, _handlers[method.Key].Serializer.ForAPI()) : null
                    }).ToArray());
        }
    }
}
