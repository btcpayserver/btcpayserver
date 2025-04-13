#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Invoices
{
    public class PaymentMethodHandlerDictionary : HandlersDictionary<PaymentMethodId, IPaymentMethodHandler>
    {
        public PaymentMethodHandlerDictionary(IEnumerable<IPaymentMethodHandler> paymentMethodHandlers) : base(paymentMethodHandlers)
        {
        }

        public object? ParsePaymentPromptDetails(PaymentPrompt prompt)
        {
            if (prompt.Details is null or JToken { Type: JTokenType.Null })
                return null;
            if (!this.TryGetValue(prompt.PaymentMethodId, out var handler))
                return null;
            return handler.ParsePaymentPromptDetails(prompt.Details);
        }
    }
}
