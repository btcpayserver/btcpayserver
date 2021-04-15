using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Security;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.HostedServices
{
    public class CssThemeManager
    {
        private readonly ISettingsRepository _settingsRepository;
        
        private async Task<ThemeSettings> GetThemeSettings()
        {
            return  (await _settingsRepository.GetSettingAsync<ThemeSettings>()) ?? new ThemeSettings();
        }

        public async Task<string> ThemeUri()
        {
            var data = await GetThemeSettings();
            return string.IsNullOrWhiteSpace(data.ThemeCssUri) ? "/main/themes/default.css" : data.ThemeCssUri;
        }

        public async Task<string> BootstrapUri()
        {
            var data = await GetThemeSettings();
            return string.IsNullOrWhiteSpace(data.BootstrapCssUri)
                ? "/main/bootstrap/bootstrap.css"
                : data.BootstrapCssUri;
        }

        public async Task<string> CreativeStartUri()
        {
            var data = await GetThemeSettings();
            return string.IsNullOrWhiteSpace(data.CreativeStartCssUri)
                ? "/main/bootstrap4-creativestart/creative.css"
                : data.CreativeStartCssUri;
        }

        public async Task<string> CustomThemeUri()
        {
            var data = await GetThemeSettings();
            return string.IsNullOrWhiteSpace(data.CustomThemeCssUri) ? null : data.CustomThemeCssUri;
        }

        public async Task<bool> FirstRun()
        {
            var data = await GetThemeSettings();
            return data.FirstRun;
        }

        public CssThemeManager(ISettingsRepository settingsRepository)
        {
            _settingsRepository = settingsRepository;
        }
    }

    public class ContentSecurityPolicyCssThemeManager : Attribute, IAsyncActionFilter, IOrderedFilter
    {
        public int Order => 1001;

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var manager = context.HttpContext.RequestServices.GetService(typeof(CssThemeManager)) as CssThemeManager;
            var policies =
                context.HttpContext.RequestServices.GetService(typeof(ContentSecurityPolicies)) as
                    ContentSecurityPolicies;
            if (manager != null && policies != null)
            {
                var creativeStartUri = await manager.CreativeStartUri();
                if (creativeStartUri != null && Uri.TryCreate(creativeStartUri, UriKind.Absolute, out var uri))
                {
                    policies.Clear();
                }

                var bootstrapUri = await manager.BootstrapUri();
                if (bootstrapUri != null && Uri.TryCreate(bootstrapUri, UriKind.Absolute, out uri))
                {
                    policies.Clear();
                }

                var themeUri = await manager.ThemeUri();
                if (themeUri != null && Uri.TryCreate(themeUri, UriKind.Absolute, out uri))
                {
                    policies.Clear();
                }

                var customThemeUri = await manager.CustomThemeUri();
                if (customThemeUri != null && Uri.TryCreate(customThemeUri, UriKind.Absolute, out uri))
                {
                    policies.Clear();
                }
            }
        }
    }
}
