using System;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Filters
{
    public class OnlyIfSupportAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _paymentMethodId;

        public OnlyIfSupportAttribute(string paymentMethodId)
        {
            _paymentMethodId = paymentMethodId;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var handlers = context.HttpContext.RequestServices.GetService<PaymentMethodHandlerDictionary>();
            if (!handlers.Support(PaymentMethodId.Parse(_paymentMethodId)))
            {
                context.Result = new NotFoundResult();
                return;
            }
            await next();
        }
    }
}
