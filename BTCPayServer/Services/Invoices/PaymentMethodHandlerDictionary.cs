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
    public class PaymentMethodHandlerDictionary : IEnumerable<IPaymentMethodHandler>
    {
        private readonly Dictionary<PaymentMethodId, IPaymentMethodHandler> _mappedHandlers =
            new Dictionary<PaymentMethodId, IPaymentMethodHandler>();

        public PaymentMethodHandlerDictionary(IEnumerable<IPaymentMethodHandler> paymentMethodHandlers)
        {
            foreach (var paymentMethodHandler in paymentMethodHandlers)
            {
				_mappedHandlers.Add(paymentMethodHandler.PaymentMethodId, paymentMethodHandler);
			}
        }

        public bool TryGetValue(PaymentMethodId paymentMethodId, [MaybeNullWhen(false)] out IPaymentMethodHandler value)
        {
            ArgumentNullException.ThrowIfNull(paymentMethodId);
            return _mappedHandlers.TryGetValue(paymentMethodId, out value);
        }
		public IPaymentMethodHandler? TryGet(PaymentMethodId paymentMethodId)
		{
			ArgumentNullException.ThrowIfNull(paymentMethodId);
			_mappedHandlers.TryGetValue(paymentMethodId, out var value);
			return value;
		}

		public IPaymentMethodHandler this[PaymentMethodId index] => _mappedHandlers[index];
        public bool Support(PaymentMethodId paymentMethod) => _mappedHandlers.ContainsKey(paymentMethod);
        public IEnumerator<IPaymentMethodHandler> GetEnumerator()
        {
            return _mappedHandlers.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
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
