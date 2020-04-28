using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    namespace BTCPayServer.Controllers.RestApi
    {
        [ApiController]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public class GreenFieldPaymentTypesController : ControllerBase
        {
            private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;

            public GreenFieldPaymentTypesController(PaymentMethodHandlerDictionary paymentMethodHandlerDictionary)
            {
                _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
            }

            [HttpGet("~/api/v1/payment-types")]
            public ActionResult<IEnumerable<string>> GetPaymentTypes()
            {
                return Ok(_paymentMethodHandlerDictionary.SelectMany(handler =>
                    handler.GetSupportedPaymentMethods().Select(id => id.PaymentType.ToString()).Distinct()));
            }

            [HttpGet("~/api/v1/payment-types/{paymentType}")]
            public ActionResult<IEnumerable<string>> GetPaymentMethodsForType(string paymentType)
            {
                if (!PaymentTypes.TryParse(paymentType, out var type))
                {
                    return NotFound();
                }

                return Ok(
                    _paymentMethodHandlerDictionary.SelectMany(handler => handler.GetSupportedPaymentMethods())
                    .Where(id => id.PaymentType == type)
                    .Select(id => id.ToString())
                    .Distinct());
            }
        }
    }
}
