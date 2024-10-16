#nullable enable
using System.Collections.Generic;
using BTCPayServer.Payments;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Services
{
    public class PrettyNameProvider
    {
        public static string GetTranslationKey(PaymentMethodId paymentMethodId) => $"PrettyName({paymentMethodId})";
        private readonly IStringLocalizer _stringLocalizer;

        public PrettyNameProvider(IStringLocalizer stringLocalizer)
        {
            _stringLocalizer = stringLocalizer;
        }
        public string PrettyName(PaymentMethodId paymentMethodId)
        {
            if (paymentMethodId is null)
                return "<NULL>";
            var key = GetTranslationKey(paymentMethodId);
            var result = _stringLocalizer[key]?.Value;
            if (string.IsNullOrEmpty(result) || result == key)
                return paymentMethodId.ToString();
            return result;
        }
    }
}
