#nullable enable
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Translations;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer;

// We use a partial class here to avoid breaking existing plugins
public static partial class Extensions
{
    public static IServiceCollection AddDefaultTranslations(this IServiceCollection services, params string[] keyValues)
    {
        return services.AddDefaultTranslations(keyValues.Select(k => KeyValuePair.Create<string, string?>(k, string.Empty)).ToArray());
    }
    public static IServiceCollection AddDefaultPrettyName(this IServiceCollection services, PaymentMethodId paymentMethodId, string defaultPrettyName)
    {
        services.AddSingleton<PrettyNameProvider.UntranslatedPrettyName>(new PrettyNameProvider.UntranslatedPrettyName(paymentMethodId, defaultPrettyName));
        return services.AddDefaultTranslations(KeyValuePair.Create<string, string?>(PrettyNameProvider.GetTranslationKey(paymentMethodId), defaultPrettyName));
    }
    public static IServiceCollection AddDefaultTranslations(this IServiceCollection services, params KeyValuePair<string, string?>[] keyValues)
    {
        services.AddSingleton<IDefaultTranslationProvider>(new InMemoryDefaultTranslationProvider(keyValues));
        return services;
    }
}
