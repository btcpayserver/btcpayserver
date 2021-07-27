using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Services;

namespace BTCPayServer
{
    public static class SettingsRepositoryExtensions
    {
        public static async Task<PoliciesSettings> GetPolicies(this ISettingsRepository settingsRepository)
        {
            return (await settingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
        }
        public static async Task<ThemeSettings> GetTheme(this ISettingsRepository settingsRepository)
        {
            var result = await settingsRepository.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();
            result.ThemeCssUri = string.IsNullOrWhiteSpace(result.ThemeCssUri) ? "/main/themes/default.css" : result.ThemeCssUri;
            result.CustomThemeCssUri = string.IsNullOrWhiteSpace(result.CustomThemeCssUri) ? null : result.CustomThemeCssUri;
            result.BootstrapCssUri = string.IsNullOrWhiteSpace(result.BootstrapCssUri) ? "/main/bootstrap/bootstrap.css" : result.BootstrapCssUri;
            result.CreativeStartCssUri = string.IsNullOrWhiteSpace(result.CreativeStartCssUri) ? "/main/bootstrap/creative.css" : result.CreativeStartCssUri;
            return result;
        }
    }
}
