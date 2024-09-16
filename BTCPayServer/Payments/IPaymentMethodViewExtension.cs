#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Payments
{
    public interface IPaymentMethodViewExtension
    {
        PaymentMethodId PaymentMethodId { get; }
        void RegisterViews(PaymentMethodViewContext context);
    }
    public record ViewViewModel(object View, object ViewModel);
    public class PaymentMethodViewProvider
    {
        private readonly Dictionary<PaymentMethodId, IPaymentMethodViewExtension> _extensions;
        private readonly PaymentMethodHandlerDictionary _handlers;

        public PaymentMethodViewProvider(
            IEnumerable<IPaymentMethodViewExtension> extensions,
            PaymentMethodHandlerDictionary handlers)
        {
            _extensions = extensions.ToDictionary(o => o.PaymentMethodId, o => o);
            _handlers = handlers;
        }
        public ViewViewModel? TryGetViewViewModel(PaymentPrompt paymentPrompt, string key)
        {
            if (!_extensions.TryGetValue(paymentPrompt.PaymentMethodId, out var extension))
                return null;
            if (!_handlers.TryGetValue(paymentPrompt.PaymentMethodId, out var handler) || paymentPrompt.Details is null)
                return null;
            var ctx = new PaymentMethodViewContext()
            {
                Details = handler.ParsePaymentPromptDetails(paymentPrompt.Details)
            };
            extension.RegisterViews(ctx);
            object? view = null;
            if (!ctx._Views.TryGetValue(key, out view))
                return null;
            return new ViewViewModel(view, handler.ParsePaymentPromptDetails(paymentPrompt.Details));
        }
    }
    public class PaymentMethodViewContext
    {
        internal Dictionary<string, object> _Views = new Dictionary<string, object>();

        public object? Details { get; internal set; }

        public void RegisterPaymentMethodDetails(string partialName)
        {
            _Views.Add("AdditionalPaymentMethodDetails", partialName);
        }
        public void RegisterCheckoutUI(CheckoutUIPaymentMethodSettings settings)
        {
            _Views.Add("CheckoutUI", settings);
        }
        public void Register(string key, object value)
        {
            _Views.Add(key, value);
        }
    }
    public class CheckoutUIPaymentMethodSettings
    {
        public string? ExtensionPartial { get; set; }
        public string? CheckoutBodyVueComponentName { get; set; }
        public string? CheckoutHeaderVueComponentName { get; set; }
        public string? NoScriptPartialName { get; set; }
    }
}
