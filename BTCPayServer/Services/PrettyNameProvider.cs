#nullable enable
using System.Collections.Generic;
using BTCPayServer.Payments;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Services
{
    public class PrettyNameProvider
    {
        public record UntranslatedPrettyName(PaymentMethodId PaymentMethodId, string Text);
        public static string GetTranslationKey(PaymentMethodId paymentMethodId) => $"PrettyName({paymentMethodId})";
        private readonly IStringLocalizer _stringLocalizer;
        Dictionary<PaymentMethodId, string> _untranslated = new Dictionary<PaymentMethodId, string>();
        public PrettyNameProvider(IEnumerable<UntranslatedPrettyName> untranslatedPrettyNames, IStringLocalizer stringLocalizer)
        {
            _stringLocalizer = stringLocalizer;
            foreach (var e in untranslatedPrettyNames)
                _untranslated.TryAdd(e.PaymentMethodId, e.Text);
        }
        public string PrettyName(PaymentMethodId paymentMethodId, bool untranslated)
        {
            if (paymentMethodId is null)
                return "<NULL>";
            if (untranslated)
            {
                if (_untranslated.TryGetValue(paymentMethodId, out var v))
                    return v;
                return paymentMethodId.ToString();
            }
            else
            {
                var key = GetTranslationKey(paymentMethodId);
                var result = _stringLocalizer[key]?.Value;
                if (string.IsNullOrEmpty(result) || result == key)
                    return paymentMethodId.ToString();
                return result;
            }
        }
        public string PrettyName(PaymentMethodId paymentMethodId) => PrettyName(paymentMethodId, false);
    }
}
