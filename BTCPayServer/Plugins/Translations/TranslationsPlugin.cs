using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Translations;

public class TranslationsPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Translations";
    public override string Identifier => "BTCPayServer.Plugins.Translations";
    public override string Name => "Translations";
    public override string Description => "Allows you to translate BTCPay Server backend";

    public override void Execute(IServiceCollection services)
    {
        services.TryAddSingleton<ViewLocalizer>();
        services.TryAddSingleton<IStringLocalizerFactory, LocalizerFactory>();
        services.TryAddSingleton<IHtmlLocalizerFactory, LocalizerFactory>();
        services.TryAddSingleton<LocalizerService>();
        services.TryAddSingleton<LanguagePackUpdateService>();
        services.AddStartupTask<LoadTranslationsStartupTask>();
        services.TryAddSingleton<IStringLocalizer>(o => o.GetRequiredService<IStringLocalizerFactory>().Create("", ""));
    }
}
