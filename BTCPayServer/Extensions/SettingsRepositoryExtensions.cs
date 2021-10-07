using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Services;

namespace BTCPayServer
{
    public static class SettingsRepositoryExtensions
    {
        public static async Task<PoliciesSettings> GetPolicies(this ISettingsRepository settingsRepository)
        {
            return await settingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
        }
        public static async Task<ThemeSettings> GetTheme(this ISettingsRepository settingsRepository)
        {
            return await settingsRepository.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();
        }
    }
}
