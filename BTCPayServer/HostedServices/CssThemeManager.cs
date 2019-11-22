using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using Microsoft.Extensions.Hosting;
using NBXplorer;
using NBXplorer.Models;
using System.Collections.Concurrent;
using BTCPayServer.Events;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.HostedServices
{
    public class CssThemeManager
    {
        public void Update(ThemeSettings data)
        {
            if (String.IsNullOrWhiteSpace(data.ThemeCssUri))
                _themeUri = "/main/themes/classic.css";
            else
                _themeUri = data.ThemeCssUri;

            if (String.IsNullOrWhiteSpace(data.CustomThemeCssUri))
                _customThemeUri = null;
            else
                _customThemeUri = data.CustomThemeCssUri;

            if (String.IsNullOrWhiteSpace(data.BootstrapCssUri))
                _bootstrapUri = "/main/bootstrap/bootstrap.css";
            else
                _bootstrapUri = data.BootstrapCssUri;

            if (String.IsNullOrWhiteSpace(data.CreativeStartCssUri))
                _creativeStartUri = "/main/bootstrap4-creativestart/creative.css";
            else
                _creativeStartUri = data.CreativeStartCssUri;

            FirstRun = data.FirstRun;
        }

        private string _themeUri;
        public string ThemeUri
        {
            get { return _themeUri; }
        }

        private string _customThemeUri;
        public string CustomThemeUri
        {
            get { return _customThemeUri; }
        }

        private string _bootstrapUri;
        public string BootstrapUri
        {
            get { return _bootstrapUri; }
        }

        private string _creativeStartUri;
        public string CreativeStartUri
        {
            get { return _creativeStartUri; }
        }


        public bool ShowRegister { get; set; }
        public bool DiscourageSearchEngines { get; set; }

        public AppType? RootAppType { get; set; }
        public string RootAppId { get; set; }

        public bool FirstRun { get; set; }

        public List<PoliciesSettings.DomainToAppMappingItem> DomainToAppMapping { get; set; } = new List<PoliciesSettings.DomainToAppMappingItem>();

        internal void Update(PoliciesSettings data)
        {
            ShowRegister = !data.LockSubscription;
            DiscourageSearchEngines = data.DiscourageSearchEngines;

            RootAppType = data.RootAppType;
            RootAppId = data.RootAppId;
            DomainToAppMapping = data.DomainToAppMapping;
            AllowLightningInternalNodeForAll = data.AllowLightningInternalNodeForAll;
        }

        public bool AllowLightningInternalNodeForAll { get; set; }
    }

    public class ContentSecurityPolicyCssThemeManager : Attribute, IActionFilter, IOrderedFilter
    {
        public int Order => 1001;

        public void OnActionExecuted(ActionExecutedContext context)
        {

        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var manager = context.HttpContext.RequestServices.GetService(typeof(CssThemeManager)) as CssThemeManager;
            var policies = context.HttpContext.RequestServices.GetService(typeof(ContentSecurityPolicies)) as ContentSecurityPolicies;
            if (manager != null && policies != null)
            {
                if(manager.CreativeStartUri != null && Uri.TryCreate(manager.CreativeStartUri, UriKind.Absolute, out var uri))
                {
                    policies.Clear();
                }
                if (manager.BootstrapUri != null && Uri.TryCreate(manager.BootstrapUri, UriKind.Absolute, out uri))
                {
                    policies.Clear();
                }
                if (manager.ThemeUri != null && Uri.TryCreate(manager.ThemeUri, UriKind.Absolute, out uri))
                {
                    policies.Clear();
                }
                if (manager.CustomThemeUri != null && Uri.TryCreate(manager.CustomThemeUri, UriKind.Absolute, out uri))
                {
                    policies.Clear();
                }
            }
        }
    }

    public class CssThemeManagerHostedService : BaseAsyncService
    {
        private SettingsRepository _SettingsRepository;
        private CssThemeManager _CssThemeManager;

        public CssThemeManagerHostedService(SettingsRepository settingsRepository, CssThemeManager cssThemeManager)
        {
            _SettingsRepository = settingsRepository;
            _CssThemeManager = cssThemeManager;
        }

        internal override Task[] InitializeTasks()
        {
            return new[]
            {
                CreateLoopTask(ListenForThemeChanges),
                CreateLoopTask(ListenForPoliciesChanges),
            };
        }

        async Task ListenForPoliciesChanges()
        {
            var data = (await _SettingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
            _CssThemeManager.Update(data);
            await _SettingsRepository.WaitSettingsChanged<PoliciesSettings>(Cancellation);
        }

        async Task ListenForThemeChanges()
        {
            var data = (await _SettingsRepository.GetSettingAsync<ThemeSettings>()) ?? new ThemeSettings();
            _CssThemeManager.Update(data);

            await _SettingsRepository.WaitSettingsChanged<ThemeSettings>(Cancellation);
        }
    }
}
